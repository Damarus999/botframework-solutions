﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Newtonsoft.Json;
using PointOfInterestSkill.Models;
using PointOfInterestSkill.Responses.Shared;

namespace PointOfInterestSkill.Services
{
    public sealed class AzureMapsGeoSpatialService : IGeoSpatialService
    {
        private static readonly int ImageWidth = 440;
        private static readonly int ImageHeight = 240;
        private static readonly int DefaultZoom = 14;
        private static readonly string FindByFuzzyQueryNoCoordinatesApiUrl = $"https://atlas.microsoft.com/search/fuzzy/json?api-version=1.0&query={{0}}&limit={{1}}";
        private static readonly string FindByFuzzyQueryApiUrl = $"https://atlas.microsoft.com/search/fuzzy/json?api-version=1.0&lat={{0}}&lon={{1}}&query={{2}}&radius={{3}}&limit={{4}}";
        private static readonly string FindByAddressQueryUrl = $"https://atlas.microsoft.com/search/address/json?api-version=1.0&lat={{0}}&lon={{1}}&query={{2}}&radius={{3}}&limit={{4}}";
        private static readonly string FindByAddressNoCoordinatesQueryUrl = $"https://atlas.microsoft.com/search/address/json?api-version=1.0&query={{0}}&limit={{1}}";
        private static readonly string FindAddressByCoordinateUrl = $"https://atlas.microsoft.com/search/address/reverse/json?api-version=1.0&query={{0}},{{1}}";
        private static readonly string FindNearbyUrl = $"https://atlas.microsoft.com/search/nearby/json?api-version=1.0&lat={{0}}&lon={{1}}&radius={{2}}&limit={{3}}";
        private static readonly string FindByCategoryUrl = $"https://atlas.microsoft.com/search/poi/category/json?api-version=1.0&query={{2}}&lat={{0}}&lon={{1}}&radius={{3}}&limit={{4}}";
        private static readonly string PinStyle = "default|la15+50|al0.75|cod83b01";
        private static readonly string ImageUrlByPoint = $"https://atlas.microsoft.com/map/static/png?api-version=1.0&layer=basic&style=main&zoom={{2}}&center={{0}},{{1}}&width={{3}}&height={{4}}&pins={PinStyle}||{{0}} {{1}}";
        private static readonly string ImageUrlForPoints = $"https://atlas.microsoft.com/map/static/png?api-version=1.0&layer=basic&style=main&zoom={{2}}&center={{0}},{{1}}&width={{4}}&height={{5}}&pins={PinStyle}|{{3}}";
        private static readonly string ImageUrlForRoute = $"https://atlas.microsoft.com/map/static/png?api-version=1.0&layer=basic&style=main&zoom={{0}}&center={{1}},{{2}}&width={{5}}&height={{6}}&pins={{3}}&path=lw2|lc0078d4|{{4}}";
        private static readonly string RoutePins = $"{PinStyle}||'{{0}}'{{1}} {{2}}|'{{3}}'{{4}} {{5}}";
        private static readonly string GetRouteDirections = $"https://atlas.microsoft.com/route/directions/json?&api-version=1.0&instructionsType=text&query={{0}}&maxAlternatives={{1}}";
        private static readonly string GetRouteDirectionsWithRouteType = $"https://atlas.microsoft.com/route/directions/json?&api-version=1.0&instructionsType=text&query={{0}}&&routeType={{1}}&maxAlternatives={{2}}";
        private static string apiKey;
        private static string userLocale;
        private static HttpClient httpClient;

        /// <summary>
        /// The maximum radius value for Azure Maps is 50,000 meters.
        /// </summary>
        private int radius;

        /// <summary>
        /// The maxium limit of points of interest for Forsquare is 100.
        /// </summary>
        private int limit;

        private int routeLimit;

        public string Provider { get { return PointOfInterestModel.AzureMaps; } }

        public Task<IGeoSpatialService> InitClientAsync(string clientId, string clientSecret, int radiusConfiguration, int limitConfiguration, int routeLimitConfiguration, string locale = "en-us", HttpClient client = null)
        {
            throw new NotImplementedException();
        }

        public Task<IGeoSpatialService> InitKeyAsync(string key, int radiusConfiguration, int limitConfiguration, int routeLimitConfiguration, string locale = "en-us", HttpClient client = null)
        {
            try
            {
                apiKey = key;
                userLocale = locale;
                radius = radiusConfiguration;
                limit = limitConfiguration;
                routeLimit = routeLimitConfiguration;

                if (client == null)
                {
                    httpClient = ServiceHelper.GetHttpClient();
                }
                else
                {
                    httpClient = client;
                }
            }
            catch (Exception)
            {
            }

            return Task.FromResult(this as IGeoSpatialService);
        }

        /// <summary>
        /// Get points of interest weighted by coordinates and using a free for search query.
        /// </summary>
        /// <param name="latitude">The current latitude.</param>
        /// <param name="longitude">The current longitude.</param>
        /// <param name="query">The search query.</param>
        /// <param name="poiType">The poi type.</param>
        /// <returns>List of PointOfInterestModels.</returns>
        public async Task<List<PointOfInterestModel>> GetPointOfInterestListByQueryAsync(double latitude, double longitude, string query, string poiType = null)
        {
            if (string.IsNullOrEmpty(query))
            {
                throw new ArgumentNullException(nameof(query));
            }

            if (double.IsNaN(latitude) || double.IsNaN(longitude))
            {
                // If missing either coordinate, the skill needs to run a fuzzy search on the query
                return await GetPointsOfInterestAsync(string.Format(CultureInfo.InvariantCulture, FindByFuzzyQueryNoCoordinatesApiUrl, query, limit), poiType);
            }

            return await GetPointsOfInterestAsync(string.Format(CultureInfo.InvariantCulture, FindByFuzzyQueryApiUrl, latitude, longitude, query, radius, limit), poiType);
        }

        /// <summary>
        /// Get coordinates from a street address.
        /// </summary>
        /// <param name="latitude">The current latitude.</param>
        /// <param name="longitude">The current longitude.</param>
        /// <param name="address">The search address.</param>
        /// <param name="poiType">The poi type.</param>
        /// <returns>List of PointOfInterestModels.</returns>
        public async Task<List<PointOfInterestModel>> GetPointOfInterestListByAddressAsync(double latitude, double longitude, string address, string poiType = null)
        {
            if (string.IsNullOrEmpty(address))
            {
                throw new ArgumentNullException(nameof(address));
            }

            if (double.IsNaN(latitude) || double.IsNaN(longitude))
            {
                // If missing either coordinate, the skill needs to run an address search on the query
                return await GetPointsOfInterestAsync(string.Format(CultureInfo.InvariantCulture, FindByAddressNoCoordinatesQueryUrl, Uri.EscapeDataString(address), limit), poiType);
            }

            return await GetPointsOfInterestAsync(string.Format(CultureInfo.InvariantCulture, FindByAddressQueryUrl, latitude, longitude, Uri.EscapeDataString(address), radius, limit), poiType);
        }

        /// <summary>
        /// Get a street address from coordinates.
        /// </summary>
        /// <param name="latitude">The current latitude.</param>
        /// <param name="longitude">The current longitude.</param>
        /// <param name="poiType">The poi type.</param>
        /// <returns>List of PointOfInterestModels.</returns>
        public async Task<List<PointOfInterestModel>> GetPointOfInterestByCoordinatesAsync(double latitude, double longitude, string poiType = null)
        {
            return await GetPointsOfInterestAsync(
                string.Format(CultureInfo.InvariantCulture, FindAddressByCoordinateUrl, latitude, longitude, radius, limit), poiType);
        }

        /// <summary>
        /// Get Point of Interest results around a specific location.
        /// </summary>
        /// <param name="latitude">The current latitude.</param>
        /// <param name="longitude">The current longitude.</param>
        /// <param name="poiType">The poi type.</param>
        /// <returns>List of PointOfInterestModels.</returns>
        public async Task<List<PointOfInterestModel>> GetNearbyPointOfInterestListAsync(double latitude, double longitude, string poiType = null)
        {
            return await GetPointsOfInterestAsync(
                string.Format(CultureInfo.InvariantCulture, FindNearbyUrl, latitude, longitude, radius, limit), poiType);
        }

        /// <summary>
        /// Get Point of Interest parking results around a specific location.
        /// </summary>
        /// <param name="latitude">The current latitude.</param>
        /// <param name="longitude">The current longitude.</param>
        /// <param name="poiType">The poi type.</param>
        /// <returns>List of PointOfInterestModels.</returns>
        public async Task<List<PointOfInterestModel>> GetPointOfInterestListByParkingCategoryAsync(double latitude, double longitude, string poiType = null)
        {
            // Available categories described at https://docs.microsoft.com/en-us/azure/azure-maps/supported-search-categories
            var parkingCategory = "OPEN_PARKING_AREA,PARKING_GARAGE";

            return await GetPointsOfInterestAsync(
                string.Format(CultureInfo.InvariantCulture, FindByCategoryUrl, latitude, longitude, parkingCategory, radius, limit), poiType);
        }

        /// <summary>
        /// Get a static map image URL of the Point of Interest and returns PointOfInterestModel.
        /// </summary>
        /// <param name="pointOfInterest">The point of interest model.</param>
        /// <returns>PointOfInterestModel.</returns>
        public Task<PointOfInterestModel> GetPointOfInterestDetailsAsync(PointOfInterestModel pointOfInterest, int width = 0, int height = 0)
        {
            string imageUrl = string.Format(
                CultureInfo.InvariantCulture,
                ImageUrlByPoint,
                pointOfInterest?.Geolocation?.Longitude,
                pointOfInterest?.Geolocation?.Latitude,
                DefaultZoom,
                width <= 0 ? ImageWidth : width,
                height <= 0 ? ImageHeight : height) + "&subscription-key=" + apiKey;

            pointOfInterest.PointOfInterestImageUrl = imageUrl;

            return Task.FromResult(pointOfInterest);
        }

        /// <summary>
        /// Get Azure Maps route based on available parameters.
        /// </summary>
        /// <param name="currentLatitude">The current latitude.</param>
        /// <param name="currentLongitude">The current longitude.</param>
        /// <param name="destinationLatitude">The destination latitude.</param>
        /// <param name="destinationLongitude">The destination longitude.</param>
        /// <param name="routeType">The (optional) route type.</param>
        /// <returns>RouteDirections.</returns>
        public async Task<RouteDirections> GetRouteDirectionsToDestinationAsync(double currentLatitude, double currentLongitude, double destinationLatitude, double destinationLongitude, string routeType = null)
        {
            // maxAlternatives are ones except for the default result. so minus 1
            if (string.IsNullOrEmpty(routeType))
            {
                return await GetRouteDirectionsAsync(string.Format(CultureInfo.InvariantCulture, GetRouteDirections, currentLatitude + "," + currentLongitude + ":" + destinationLatitude + "," + destinationLongitude, routeLimit - 1) + "&subscription-key=" + apiKey);
            }
            else
            {
                return await GetRouteDirectionsAsync(string.Format(CultureInfo.InvariantCulture, GetRouteDirectionsWithRouteType, currentLatitude + "," + currentLongitude + ":" + destinationLatitude + "," + destinationLongitude, routeType, routeLimit - 1) + "&subscription-key=" + apiKey);
            }
        }

        public async Task<string> GetRouteImageAsync(PointOfInterestModel destination, RouteDirections.Route route, int width = 0, int height = 0)
        {
            var latLngs = new List<LatLng>();

            // TODO or we could use Data in Azure Maps Service to store the whole path
            var sb = new StringBuilder();
            int pointsTotal = route.Legs.Sum(leg => leg.Points.Length);
            int step = Math.Max(1, pointsTotal / 10);
            int id = 0;
            foreach (var leg in route.Legs)
            {
                while (true)
                {
                    if (id >= leg.Points.Length)
                    {
                        id -= leg.Points.Length;
                        break;
                    }

                    AddPoint(leg.Points[id].Longitude, leg.Points[id].Latitude);
                    id += step;
                }
            }

            AddPoint(destination.Geolocation.Longitude, destination.Geolocation.Latitude);

            width = width <= 0 ? ImageWidth : width;
            height = height <= 0 ? ImageHeight : height;

            (double centerLongitude, double centerLatitude, int zoom) = GetCoveredLocationZoom(latLngs, width, height);

            string pins = string.Format(CultureInfo.InvariantCulture, RoutePins, PointOfInterestSharedStrings.START, route.Legs[0].Points[0].Longitude, route.Legs[0].Points[0].Latitude, PointOfInterestSharedStrings.END, destination.Geolocation.Longitude, destination.Geolocation.Latitude);

            return string.Format(CultureInfo.InvariantCulture, ImageUrlForRoute, zoom, centerLongitude, centerLatitude, pins, sb.ToString(), width, height) + "&subscription-key=" + apiKey;

            void AddPoint(double longitude, double latitude)
            {
                sb.Append($"|{longitude} {latitude}");
                latLngs.Add(new LatLng
                {
                    Latitude = latitude,
                    Longitude = longitude
                });
            }
        }

        public async Task<string> GetAllPointOfInterestsImageAsync(LatLng currentCoordinates, List<PointOfInterestModel> pointOfInterestModels, int width = 0, int height = 0)
        {
            var latLngs = pointOfInterestModels.Select(model => model.Geolocation).ToList();
            if (currentCoordinates != null)
            {
                latLngs.Add(currentCoordinates);
            }

            width = width <= 0 ? ImageWidth : width;
            height = height <= 0 ? ImageHeight : height;

            (double centerLongitude, double centerLatitude, int zoom) = GetCoveredLocationZoom(latLngs, width, height);

            var sb = new StringBuilder();
            foreach (var model in pointOfInterestModels)
            {
                // TODO better idea? '(%2527) is not supported.
                sb.Append($"|'{HttpUtility.UrlEncode(HttpUtility.UrlEncode(model.Name.Replace("'", string.Empty)))}'{model.Geolocation.Longitude} {model.Geolocation.Latitude}");
            }

            if (currentCoordinates != null)
            {
                sb.Append($"|'{PointOfInterestSharedStrings.YOU}'{currentCoordinates.Longitude} {currentCoordinates.Latitude}");
            }

            return string.Format(CultureInfo.InvariantCulture, ImageUrlForPoints, centerLongitude, centerLatitude, zoom, sb.ToString(), width, height) + "&subscription-key=" + apiKey;
        }

        /// <summary>
        /// Get longtitude, latitude, zoom that cover input.
        /// </summary>
        /// <param name="latLngs">Input locations.</param>
        /// <returns>Longtitude, latitude, zoom.</returns>
        private (double, double, int) GetCoveredLocationZoom(IList<LatLng> latLngs, int width, int height)
        {
            double maxLongitude = -180;
            double minLongitude = 180;
            double maxLatitude = -90;
            double minLatitude = 90;

            foreach (var latLng in latLngs)
            {
                maxLongitude = Math.Max(maxLongitude, latLng.Longitude);
                minLongitude = Math.Min(minLongitude, latLng.Longitude);
                maxLatitude = Math.Max(maxLatitude, latLng.Latitude);
                minLatitude = Math.Min(minLatitude, latLng.Latitude);
            }

            double centerLongitude = (maxLongitude + minLongitude) * 0.5;
            double longitudeDifference = maxLongitude - minLongitude;
            if (longitudeDifference > 180)
            {
                centerLongitude = centerLongitude >= 0 ? centerLongitude - 180 : centerLongitude + 180;
                longitudeDifference = 180 - longitudeDifference;
            }

            double centerLatitude = (maxLatitude + minLatitude) * 0.5;
            double latitudeDifference = maxLatitude - minLatitude;
            int pinBuffer = 10;
            double longitudeZoom = Math.Log((width - (2 * pinBuffer)) * 360.0 / 512.0 / longitudeDifference, 2);
            double latitudeZoom = Math.Log((height - (2 * pinBuffer)) * 180.0 / 512.0 / latitudeDifference, 2);
            int zoom = (int)Math.Min(longitudeZoom, latitudeZoom);

            // all differences are zero
            if (zoom < 0)
            {
                zoom = DefaultZoom;
            }

            // maximum level
            // https://docs.microsoft.com/en-us/azure/azure-maps/zoom-levels-and-tile-grid?tabs=csharp
            if (zoom > 24)
            {
                zoom = 24;
            }

            return (centerLongitude, centerLatitude, zoom);
        }

        /// <summary>
        /// Get route directions response from Azure Maps.
        /// </summary>
        /// <returns>RouteDirections.</returns>
        private async Task<RouteDirections> GetRouteDirectionsAsync(string url)
        {
            var response = await httpClient.GetAsync(url);

            var apiResponse = new RouteDirections();

            // TODO when it returns 400 for uncovered areas, we return no route instead. For other unsuccessful codes, exception is thrown as usual
            if (response.StatusCode != System.Net.HttpStatusCode.BadRequest)
            {
                response = response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                apiResponse = JsonConvert.DeserializeObject<RouteDirections>(content);
            }

            apiResponse.Provider = PointOfInterestModel.AzureMaps;

            return apiResponse;
        }

        /// <summary>
        /// Get search reuslts response from Azure Maps and convert to point of interest list.
        /// </summary>
        /// <returns>List of PointOfInterestModels.</returns>
        private async Task<List<PointOfInterestModel>> GetPointsOfInterestAsync(string url, string poiType = null)
        {
            url = string.Concat(url, $"&language={userLocale}&subscription-key={apiKey}");

            var response = await httpClient.GetStringAsync(url);

            var apiResponse = JsonConvert.DeserializeObject<SearchResultSet>(response);

            var pointOfInterestList = new List<PointOfInterestModel>();

            if (apiResponse?.Results != null)
            {
                if (!string.IsNullOrEmpty(poiType))
                {
                    if (poiType == GeoSpatialServiceTypes.PoiType.Nearest)
                    {
                        var nearestResult = apiResponse.Results.Aggregate((agg, next) => agg.Distance <= next.Distance ? agg : next);

                        if (nearestResult != null)
                        {
                            apiResponse.Results = new List<SearchResult> { nearestResult };
                        }
                    }
                }

                foreach (var searchResult in apiResponse.Results)
                {
                    var newPointOfInterest = new PointOfInterestModel(searchResult);

                    // If the POI list doesn't already have an item with an identical address, add it to the list
                    if (!pointOfInterestList.Any(poi => poi.Address == newPointOfInterest.Address))
                    {
                        pointOfInterestList.Add(newPointOfInterest);
                    }
                }
            }

            return pointOfInterestList;
        }
    }
}