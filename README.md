# Garmin2StravaFinalSync
When the Strava account is connected to a Garmin account, new Garmin activities are automatically uploaded to the Strava. Unfortunately, the Strava does not upload the name and description of the Garmin activity.

The single source file **Garmin2FoodFinalSync** Microsoft Windows console application updates the name and description from Garmin to Strava for all matching activities between the dates specified.

**1.** Before you begin you must register your [Strava API Application](https://www.strava.com/settings/api) to obtain a Strava Client ID and a Strava Client Secret. This is a one-time manual step. When registering the application, enter the following fields:

| Field | Value |
| --- | ----------- |
| Application Name | *enter a name of your choice, such as* YourNameStravaApiApp |
| Category | Other |
| Club | *leave empty* |
| Website | *enter a valid URL, example* http://myhomepage.com |
| Application Description | *leave empty* |
| Authorization Callback Domain | localhost |

Press [Create](https://developers.strava.com/images/getting-started-1.png) to register the application and then note the Client ID and Client Secret. In the next step, they will be inserted into the configuration file.

**2.** The application is configured by [appsettings.json](appsettings.json) file. Please enter your values:

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

**3.** When you start the application, it first loads your copy of the [configuration file](appsettings.json) and then launches the default [Strava authentication page](https://developers.strava.com/images/getting-started-4.png) using your default web browser. Press Authorize to start Garmin2FoodFinalSync synchronization.


> :memo: Please note that Strava limits API usage to a maximum of 100 requests every 15 minutes, with up to 1,000 requests per day.
