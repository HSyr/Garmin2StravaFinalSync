/*
 * Runs on MS Windows only
 * Built by Visual Studio 2022 Community
 * Target framework = .NET 6.0
 *    Nullable = Disable
 *    Implicit global usings = false
 * Installed Packages:
 *    Unofficial.Garmin.Connect
 *    Microsoft.Extensions.Configuration
 *    Microsoft.Extensions.Configuration.Json
 *    Microsoft.Extensions.Configuration.Binder
 *    Newtonsoft.Json
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;

using Garmin.Connect;
using Garmin.Connect.Auth;
using Garmin.Connect.Models;

using Microsoft.Extensions.Configuration;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

using static System.Console;

TimeSpan maxGarminStravaTimeDifference = new( 0, 5, 0 );

Thread.CurrentThread.CurrentCulture = Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;

Settings settings = new ConfigurationBuilder().AddJsonFile( "appsettings.json" ).Build().GetRequiredSection( "Settings" ).Get<Settings>();
if ( settings.DateAfter == DateTime.MinValue || settings.DateBefore == DateTime.MinValue )
{
  DateTime dateTimeNow = DateTime.Now;
  settings.DateAfter = new( dateTimeNow.Year, dateTimeNow.Month, dateTimeNow.Day );
  settings.DateBefore = settings.DateAfter.AddDays( 1 );
}

WriteLine( $"Time interval = {settings.DateAfter.ToString("yyyy-MM-dd")} - {settings.DateBefore.ToString( "yyyy-MM-dd" )}" );

// ----------------------------- Authorize to Strava -----------------------------
using HttpListener httpListener = new();
string redirectPath = $"/Temporary_Listen_Addresses/{Guid.NewGuid()}/";
httpListener.Prefixes.Add( "http://+:80" + redirectPath );

Process.Start( new ProcessStartInfo
{
  FileName = "https://www.strava.com/oauth/authorize?" +
    $"client_id={settings.StravaClientId}&" +
    $"redirect_uri=http://localhost//{redirectPath}&" +
    "response_type=code&" +
    "scope=activity:read_all,activity:write,profile:write",
  UseShellExecute = true
} );

httpListener.Start();
WriteLine( "Waiting for Strava authentication..." );

HttpListenerContext context = httpListener.GetContext();
string stravaCode = context.Request.QueryString["code"];
if ( stravaCode == null )
  throw new( "Strava 'code' missing in the http redirect query." );
WriteLine( $"Strava code = {stravaCode}" );

HttpListenerResponse response = context.Response;
byte[] buffer = Encoding.UTF8.GetBytes( "<html><body><h1>Authorization successful!</h1></body></html>" );
response.ContentLength64 = buffer.Length;
response.OutputStream.Write( buffer, 0, buffer.Length );
response.OutputStream.Close();
httpListener.Stop();

// ----------------------------- Get Strava access token -----------------------------
using HttpClient stravaHttpClient = new()
{
  BaseAddress = new Uri( "https://www.strava.com" )
};

string authUrl = "/oauth/token?" +
  $"client_id={settings.StravaClientId}&" +
  $"client_secret={settings.StravaSecret}&" +
  $"code={stravaCode}&" +
  "grant_type=authorization_code";
HttpResponseMessage getTokens = await stravaHttpClient.PostAsync( authUrl, null );
string stravaAccessToken = ( await getTokens.Content.ReadAsStringAsync() ).JsonDeserialize().access_token;
WriteLine( $"Strava access token = {stravaAccessToken}" );

// ----------------------------- Connect to Garmin -----------------------------
BasicAuthParameters authParameters = new( settings.GarminLogin, settings.GarminPassword );
using HttpClient httpClient = new();
GarminConnectClient client = new( new GarminConnectContext( httpClient, authParameters ) );

// ----------------------------- Update weight -----------------------------
if ( settings.UpdateWeight )
{
  GarminUserSettings garminUserSettings = await client.GetUserSettings();
  HttpResponseMessage resultPut = await stravaHttpClient.PutAsync(
    $"/api/v3/athlete?" +
    $"weight={garminUserSettings.UserData.Weight / 1000}&" +
    $"access_token={stravaAccessToken}", null );

  if ( resultPut.StatusCode != HttpStatusCode.OK )
    throw new( "! Error updating weight {resultPut.StatusCode}" );

  WriteLine( $"Athlete weight updated to {( garminUserSettings.UserData.Weight / 1000 ):0.0}" );
}

// ----------------------------- Read Strava activities -----------------------------
WriteLine( "Reading Strava activities, please wait..." );
List<dynamic> stravaActivities = new();
for ( int stravaActivitiesPage = 1; ; stravaActivitiesPage++ )
{
  string getActivitiesUrl = "/api/v3/athlete/activities?" +
    $"before={settings.DateBefore.DateTimeToUnixTimestamp()}&" +
    $"after={settings.DateAfter.DateTimeToUnixTimestamp()}&" +
    $"page={stravaActivitiesPage}&" +
    "per_page=200&" +
    $"access_token={stravaAccessToken}";

  HttpResponseMessage getResult = await stravaHttpClient.GetAsync( getActivitiesUrl );
  if ( getResult.StatusCode != HttpStatusCode.OK )
    throw new( $"Error {getResult.StatusCode} when reading Strava activities" );

  List<ExpandoObject> newActivities = ( await getResult.Content.ReadAsStringAsync() ).JsonDeserializeList();
  if ( newActivities.Count == 0 )
    break;

  foreach ( dynamic stravaActivity in newActivities )
    WriteLine(
      $"\t{stravaActivity.type}\t" +
      $"{stravaActivity.start_date_local}\t" +
      $"{stravaActivity.name}" );

  stravaActivities.AddRange( newActivities );
}

if ( stravaActivities.Count == 0 )
{
  WriteLine( $"No Strava activities" );
  return;
}

// ----------------------------- Read Garmin activities -----------------------------
WriteLine( "Reading Garmin activities, please wait..." );
GarminActivity[] garminActivities = await client.GetActivitiesByDate( settings.DateAfter, settings.DateBefore.AddDays( -1 ), null );
if ( garminActivities.Length == 0 )
{
  Write( $"No Garmin activities" );
  return;
}

// ----------------------------- Synchronize Name/Description from Garmin to Strava -----------------------------
foreach ( GarminActivity garminActivity in garminActivities )
{
  WriteLine(
    $"\t{garminActivity.ActivityType.TypeKey}\t" +
    $"{garminActivity.StartTimeLocal}\t" +
    $"{garminActivity.ActivityName}" );

  var foundGarminInStrava =
    from dynamic stravaActivity
    in stravaActivities
    where ( garminActivity.StartTimeGmt - stravaActivity.start_date ).Duration() < maxGarminStravaTimeDifference ||
          ( garminActivity.StartTimeLocal - stravaActivity.start_date_local ).Duration() < maxGarminStravaTimeDifference
    select stravaActivity;

  if ( foundGarminInStrava.Count() != 1 )
    WriteLine( $"\t! Garmin activity not found in Strava!" );
  else
  {
    dynamic stravaActivity = foundGarminInStrava.First();
    string stravaActivityNameTrim = stravaActivity.name.Trim();
    // Note: other character replacements might be needed
    string garminActivityNameModified = garminActivity.ActivityName.Trim().Replace( "#", "" ).Replace( '+', ' ' );

    string updateName = "";
    if ( settings.UpdateName && garminActivityNameModified != stravaActivityNameTrim )
      updateName = $"&name={garminActivityNameModified}";

    string updateDescription = "";
    if ( settings.UpdateDescription && !string.IsNullOrEmpty( garminActivity.Description ) )
    {
      string getActivitysUrl = $"api/v3/activities/{stravaActivity.id}?" +
        $"access_token={stravaAccessToken}";

      HttpResponseMessage getResult = await stravaHttpClient.GetAsync( getActivitysUrl );
      if ( getResult.StatusCode != HttpStatusCode.OK )
        throw new( $"Error {getResult.StatusCode} when reading Strava activity" );

      dynamic stravaActivityDetail = ( await getResult.Content.ReadAsStringAsync() ).JsonDeserialize();
      if ( string.Compare( garminActivity.Description, stravaActivityDetail.description ) != 0 )
        updateDescription = $"&description={garminActivity.Description}";
    }

    if ( updateName != "" || updateDescription != "" )
    {
      HttpResponseMessage resultPut = await stravaHttpClient.PutAsync(
        $"/api/v3/activities/{stravaActivity.id}?" +
        $"&access_token={stravaAccessToken}" +
        $"{updateName}" +
        $"{updateDescription}", null );

      WriteLine( resultPut.StatusCode != HttpStatusCode.OK ?
        $"\t! Error updating Strava activity {resultPut.StatusCode}!" :
        "\tStrava activity updated OK" );
    }
  }
}

internal class Settings
{
  /// <summary>
  /// Strava Client ID obtained by registering your API application at https://www.strava.com/settings/api
  /// </summary>
  public int StravaClientId { get; set; }
  /// <summary>
  /// Strava Client Secret obtained by registering your API application at https://www.strava.com/settings/api
  /// </summary>
  public string StravaSecret { get; set; }
  /// <summary>
  /// Garmin account login email
  /// </summary>
  public string GarminLogin { get; set; }
  /// <summary>
  /// Garmin account password
  /// </summary>
  public string GarminPassword { get; set; }
  /// <summary>
  /// true to update Strava activity name when the Garmin activity name is different
  /// </summary>
  public bool UpdateName { get; set; }
  /// <summary>
  /// true to update Strava activity description when the Garmin activity description is not empty and different
  /// </summary>
  public bool UpdateDescription { get; set; }
  /// <summary>
  /// true to update Strava athlete weight from Garming
  /// </summary>
  public bool UpdateWeight { get; set; }
  /// <summary>
  /// Update activities that have taken place after a certain date.
  /// If this or the following property is missing in the configuration file today is used.
  /// </summary>
  public DateTime DateAfter { get; set; }
  /// Update activities that have taken place before a certain date.
  /// If this or the previous property is missing in the configuration file tomorrow is used.
  public DateTime DateBefore { get; set; }
}

internal static class Extensions
{
  public static dynamic JsonDeserialize ( this string json ) => JsonConvert.DeserializeObject<ExpandoObject>( json, new ExpandoObjectConverter() );
  public static List<ExpandoObject> JsonDeserializeList ( this string json ) => JsonConvert.DeserializeObject<List<ExpandoObject>>( json, new ExpandoObjectConverter() );
  public static double DateTimeToUnixTimestamp ( this DateTime dateTime ) => ( TimeZoneInfo.ConvertTimeToUtc( dateTime ) - DateTime.UnixEpoch ).TotalSeconds;
}
