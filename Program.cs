/*
 * Runs on MS Windows only
 * Built by Visual Studio 2022 Community
 * Target framework = .NET 6.0
 *    Nullable = Disable
 *    Implicit global usings = false
 * Installed Packages:
 *    Unofficial.Garmin.Connect 0.2.0+
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
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web;

using Garmin.Connect;
using Garmin.Connect.Auth;
using Garmin.Connect.Models;

using Microsoft.Extensions.Configuration;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

using static System.Console;

TimeSpan maxGarminStravaTimeDifference = new( 0, 5, 0 );

Thread.CurrentThread.CurrentCulture = Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;

// ----------------------------- Read configuration -----------------------------
Settings settings = new ConfigurationBuilder().AddJsonFile( "appsettings.json" ).Build().GetRequiredSection( "Settings" ).Get<Settings>();
if ( settings.DateAfter == DateTime.MinValue || settings.DateBefore == DateTime.MinValue )
{
  DateTime dateTimeNow = DateTime.Now;
  settings.DateAfter = new( dateTimeNow.Year, dateTimeNow.Month, dateTimeNow.Day );
  settings.DateBefore = settings.DateAfter.AddDays( 1 );
}
else
{
  settings.DateAfter = new( settings.DateAfter.Year, settings.DateAfter.Month, settings.DateAfter.Day );
  settings.DateBefore = new( settings.DateBefore.Year, settings.DateBefore.Month, settings.DateBefore.Day );
}

WriteLine( $"Time interval = {settings.DateAfter:yyyy-MM-dd} - {settings.DateBefore:yyyy-MM-dd}" );

List<(string, string)> propertiesToDescription = new();
foreach ( string propertyCfg in ( settings.PropertiesToDescription ?? "" ).Split( ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ) )
{
  int formatIndex = propertyCfg.IndexOf( ':' );
  if ( formatIndex == -1 )
    propertiesToDescription.Add( (propertyCfg, null) );
  else
    propertiesToDescription.Add( (propertyCfg[..formatIndex], propertyCfg[( formatIndex + 1 )..]) );
}

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
string stravaCode = context.Request.QueryString["code"] ?? throw new( "! Strava 'code' missing in the http redirect query." );

HttpListenerResponse response = context.Response;
byte[] buffer = Encoding.UTF8.GetBytes( "<html><body><h1>Authorization successful!</h1></body></html>" );
response.ContentLength64 = buffer.Length;
response.OutputStream.Write( buffer, 0, buffer.Length );
response.OutputStream.Close();
WriteLine( $"Strava code = {stravaCode}" );
httpListener.Stop();

string[] stravaApiUsages = null, stravaApiLimits = null;
try
{
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
  HttpResponseMessage stravaResponse = await stravaHttpClient.PostAsync( authUrl, null );
  string stravaAccessToken = ( await stravaResponse.Content.ReadAsStringAsync() ).JsonDeserialize().access_token;
  WriteLine( $"Strava access token = {stravaAccessToken}" );

  // ----------------------------- Connect to Garmin -----------------------------
  BasicAuthParameters authParameters = new( settings.GarminLogin, settings.GarminPassword );
  using HttpClient httpClient = new();
  GarminConnectClient client = new( new GarminConnectContext( httpClient, authParameters ) );

  // ----------------------------- Update weight -----------------------------
  if ( settings.UpdateWeight )
  {
    GarminUserSettings garminUserSettings = await client.GetUserSettings();
    stravaResponse = await stravaHttpClient.PutAsync(
      $"/api/v3/athlete?" +
      $"weight={garminUserSettings.UserData.Weight / 1000}&" +
      $"access_token={stravaAccessToken}", null );

    if ( stravaResponse.StatusCode != HttpStatusCode.OK )
      throw new( "! Error updating weight {resultPut.StatusCode}." );

    WriteLine( $"Athlete weight updated to {( garminUserSettings.UserData.Weight / 1000 ):0.0}" );
    checkStravaApiLimits();
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

    stravaResponse = await stravaHttpClient.GetAsync( getActivitiesUrl );
    if ( stravaResponse.StatusCode != HttpStatusCode.OK )
      throw new( $"! Error {stravaResponse.StatusCode} when reading Strava activities." );

    checkStravaApiLimits();

    List<ExpandoObject> newActivities = ( await stravaResponse.Content.ReadAsStringAsync() ).JsonDeserializeList();
    if ( !newActivities.Any() )
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

  // ----------------------------- Synchronize Name and/or Description from Garmin to Strava -----------------------------
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

      string updateName = "";
      if ( settings.UpdateName && garminActivity.ActivityName != stravaActivity.name )
        updateName = $"&name={HttpUtility.UrlEncode( garminActivity.ActivityName )}";

      string updateDescription = "";
      if ( settings.UpdateDescription && ( !string.IsNullOrEmpty( garminActivity.Description ) || settings.GearsToDescription || propertiesToDescription.Any() ) )
      {
        StringBuilder garminActivityDescription = new( garminActivity.Description ?? "" );

        if ( propertiesToDescription.Any() )
        {
          foreach ( (string propertyName, string propertyFormat) in propertiesToDescription )
          {
            PropertyInfo propertyInfo = garminActivity.GetType().GetProperty( propertyName ) ?? throw new( $"! Activity property '{propertyName}' does not exist!" );
            object propertyValue = propertyInfo.GetValue( garminActivity );
            if ( propertyValue != null && ( propertyValue.GetType().IsValueType
                                            ? !propertyValue.Equals( Activator.CreateInstance( propertyValue.GetType() ) )
                                            : propertyValue != null ) )
              garminActivityDescription
                .AppendLine()
                .Append( $"{propertyName}=" )
                .Append( propertyFormat == null
                         ? propertyValue.ToString()
                         : string.Format( $"{{0:{propertyFormat}}}", propertyValue ) );
          }
        }

        if ( settings.GearsToDescription )
        {
          GarminGear[] gears = await client.GetActivityGears( garminActivity.ActivityId );
          if ( gears.Length != 0 )
          {
            garminActivityDescription
              .AppendLine()
              .AppendLine()
              .Append( "Gears:" );
            foreach ( GarminGear gear in gears )
              garminActivityDescription
                .AppendLine()
                .Append( "*  " )
                .Append( gear.DisplayName );
          }
        }

        string getActivitysUrl = $"api/v3/activities/{stravaActivity.id}?" +
          $"access_token={stravaAccessToken}";

        stravaResponse = await stravaHttpClient.GetAsync( getActivitysUrl );
        if ( stravaResponse.StatusCode != HttpStatusCode.OK )
          throw new( $"! Error {stravaResponse.StatusCode} when reading Strava activity." );

        checkStravaApiLimits();

        dynamic stravaActivityDetail = ( await stravaResponse.Content.ReadAsStringAsync() ).JsonDeserialize();
        if ( string.Compare( garminActivityDescription.ToString(), stravaActivityDetail.description ) != 0 )
          updateDescription = $"&description={HttpUtility.UrlEncode( garminActivityDescription.ToString() )}";
      }

      if ( updateName != "" || updateDescription != "" )
      {
        stravaResponse = await stravaHttpClient.PutAsync(
         $"/api/v3/activities/{stravaActivity.id}?" +
         $"&access_token={stravaAccessToken}" +
         $"{updateName}" +
         $"{updateDescription}", null );

        WriteLine( stravaResponse.StatusCode != HttpStatusCode.OK ?
          $"\t! Error updating Strava activity {stravaResponse.StatusCode}!" :
          "\tStrava activity updated OK" );

        checkStravaApiLimits();
      }
    }
  }

  void checkStravaApiLimits ()
  {
    if ( stravaResponse.Headers.TryGetValues( "X-Ratelimit-Usage", out var headersUsage ) &&
         stravaResponse.Headers.TryGetValues( "X-Ratelimit-Limit", out var headersLimit ) )
    {
      stravaApiUsages = headersUsage.First().Split( ',' );
      stravaApiLimits = headersLimit.First().Split( ',' );

      if ( int.Parse( stravaApiUsages[0] ) >= int.Parse( stravaApiLimits[0] ) )
        throw new( $"! 15-minute Strava API limit {stravaApiLimits[0]} has been exhausted." );
      if ( int.Parse( stravaApiUsages[1] ) >= int.Parse( stravaApiLimits[1] ) )
        throw new( $"! Daily Strava API limit {stravaApiLimits[1]} has been exhausted." );
    }
  }
}
finally
{
  if ( stravaApiUsages != null && stravaApiLimits != null )
    WriteLine( $"Strava API usage: 15-minute {stravaApiUsages[0]}/{stravaApiLimits[0]}, daily {stravaApiUsages[1]}/{stravaApiLimits[1]}" );
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
  /// if true than Strava activity description is also updated when <see cref="PropertiesToDescription"/> or <see cref="GearsToDescription"/> are set.
  /// </summary>
  public bool UpdateDescription { get; set; }

  /// <summary>
  /// true to append specified Garmin activity properties to the Strava activity description. Requires UpdateName = true.
  /// The value of this configuration item is the list of properties separated by semicolons. Optional formatting string follows the colon after the property name.
  /// Example: "VO2MaxValue;MaxHr;AvgStrideLength:0.0"
  /// List of Garmin activity properties: https://github.com/sealbro/dotnet.garmin.connect/blob/main/Garmin.Connect/Models/GarminActivity.cs
  /// Custom numeric format strings: https://docs.microsoft.com/en-us/dotnet/standard/base-types/custom-numeric-format-strings
  /// </summary>
  public string PropertiesToDescription { get; set; }

  /// <summary>
  /// true to append used gears to the description. Requires UpdateName = true.
  /// </summary>
  public bool GearsToDescription { get; set; }

  /// <summary>
  /// true to update Strava athlete weight from Garmin.
  /// </summary>
  public bool UpdateWeight { get; set; }

  /// <summary>
  /// Update activities that have taken place after a certain date.
  /// If this or the following property is missing in the configuration file today is used.
  /// </summary>
  public DateTime DateAfter { get; set; }

  /// <summary>
  /// Update activities that have taken place before a certain date.
  /// If this or the previous property is missing in the configuration file tomorrow is used.
  /// </summary>
  public DateTime DateBefore { get; set; }
}

internal static class Extensions
{
  public static dynamic JsonDeserialize ( this string json ) => JsonConvert.DeserializeObject<ExpandoObject>( json, new ExpandoObjectConverter() );
  public static List<ExpandoObject> JsonDeserializeList ( this string json ) => JsonConvert.DeserializeObject<List<ExpandoObject>>( json, new ExpandoObjectConverter() );
  public static double DateTimeToUnixTimestamp ( this DateTime dateTime ) => ( TimeZoneInfo.ConvertTimeToUtc( dateTime ) - DateTime.UnixEpoch ).TotalSeconds;
}
