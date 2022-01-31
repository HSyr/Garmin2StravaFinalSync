# Garmin2StravaFinalSync
When the Strava account is connected to a Garmin account, new activities in Garmin are automatically uploaded to the Strava. Unfortunately, the Strava does not upload the name and description of the Garmin activity.

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
| StravaClientId | |
| StravaSecret | |
| GarminLogin | |
| GarminPassword  | |
| UpdateName | |
| UpdateDescription | |
| UpdateWeight | |
| DateAfter | |
| DateBefore | |

Please note that Strava limits API usage to a maximum of 100 requests every 15 minutes, with up to 1,000 requests per day.
