# Garmin2StravaFinalSync
When the Strava is connected to a Garmin, new activities in Garmin are automatically uploaded to the Strava. Unfortunately, the Strava does not upload the name and description of the Garmin activity.

This single source file **Garmin2FoodFinalSync** Microsoft Windows console application updates the name and description from Garmin to Strava for all matching activities between the dates specified.

Before you start you have to register your [Strava API Application](https://www.strava.com/settings/api) to obtain Strava Client ID and Strava Client Secret. This is a one time, manual step. Enter the following fields when registering the application:

| Field | Value |
| --- | ----------- |
| Application Name | *enter name of your choice, example YourNameStravaApiApp* |
| Category | Other |
| Club | *leave empty* |
| Website | *enter valid URL, example http://myhomepage.com* |
| Application Description | *leave empty* |
| Authorization Callback Domain | localhost |

Press [Create](https://developers.strava.com/images/getting-started-2.png) to register the application and then note down Client ID and Client Secret. They will be entered into configuration file in the next step.

The application is configured by [appsettings.json](appsettings.json) file. Please enter your values:

| Property | Value |
| --- | ----------- |
| StravaClientId | Strava Client ID obtained by registering your API application. |
| StravaSecret | Strava Client Secret obtained by registering your API application. |
| GarminLogin | Garmin account login email. |
| GarminPassword | Garmin account password. |
| UpdateName | true to update Strava activity name when the Garmin activity name is different. Otherwise false. |
| UpdateDescription | true to update Strava activity description when the Garmin activity description is not empty and different. Otherwise false. |
| UpdateWeight | true to update Strava athlete weight from Garmin. Otherwise false. |
| DateAfter | Update activities that have taken place after a certain date. Example "2022-01-29". If this or the following property is missing in the configuration file today is used. |
| DateBefore | Update activities that have taken place before a certain date. Example "2022-01-30". If this or the previous property is missing in the configuration file tomorrow is used. |

When the application starts it first reads the configuration file and then launches a default [Strava authentication page](https://developers.strava.com/images/getting-started-4.png) using your default Internet browser. Press Authorize to allow Garmin2FoodFinalSync synchronizing.

Please note that Strava limits API usage to a maximum of 100 requests every 15 minutes, with up to 1,000 requests per day.
