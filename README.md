# Garmin2StravaFinalSync
When the Strava is connected to a Garmin, new activities in Garmin are automatically uploaded to the Strava. Unfortunately, the Strava does not upload the name and description of the Garmin activity.

Single source file **Garmin2FoodFinalSync** Microsoft Windows console application updates the name and description from Garmin to Strava for all matching activities between the dates specified.

Before you start you have to register you API application [Strava My API Application](https://www.strava.com/settings/api) to obtain Strava Client ID and Strava Client Secret. Enter the following fields:

| Field | Value |
| --- | ----------- |
| Application Name | *enter name of your choice, example MyGarmin2StravaFinalSyncApp* |
| Category | Other |
| Club | *leave empty* |
| Website | *enter valid URL, example http://myhomepage.com* |
| Application Description | *leave empty* |
| Authorization Callback Domain | locahost |

Register the application and mark down Client ID and Client Secret. 

The application is configured by [appsettings.json](appsettings.json) file. Enter your values:

| Field | Value |
| --- | ----------- |
| StravaClientId | Strava Client ID obtained by registering your API application. |
| StravaSecret | Strava Client Secret obtained by registering your API application. |
| GarminLogin | Garmin account login email. |
| GarminPassword | Garmin account password. |
| UpdateName | true to update Strava activity name when the Garmin activity name is different. Otherwise false. |
| UpdateDescription | true to update Strava activity description when the Garmin activity description is not empty and different. Otherwise false. |
| UpdateWeight | true to update Strava athlete weight from Garming. Otherwise false. |
| DateAfter | Update activities that have taken place after a certain date. Example "2022-01-29". If this or the following property is missing in the configuration file today is used. |
| DateBefore | Update activities that have taken place before a certain date. Example "2022-01-30". If this or the previous property is missing in the configuration file tomorrow is used. |

Please note that Strava limits API usage to a maximum of 100 requests every 15 minutes, with up to 1,000 requests per day.
