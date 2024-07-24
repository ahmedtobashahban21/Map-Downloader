using Newtonsoft.Json.Linq;
using Polly;
using Polly.Retry;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Path = System.IO.Path;
using MessageBox = System.Windows.MessageBox;
using System.Windows.Forms;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace DownloadTilesOSM
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        private bool Update = true; // this variable to check if user want to download from beginning or not ( default is redownloading )

        // initialize the client server to send the request 
        private readonly HttpClient _httpClient;
        private readonly AsyncRetryPolicy<HttpResponseMessage> _retryPolicy;
        public MainWindow()
        {
            InitializeComponent();

            _httpClient = new HttpClient
            {
                DefaultRequestHeaders =
            {
                { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36" },
                { "Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3" }
            }
            };

            _retryPolicy = Policy.HandleResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode)
                                 .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
        }

        //  functions to browse files

        private void BrowseOutputDirectory_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    OutputPathTextBox.Text = dialog.SelectedPath;
                }
                else
                {
                    // Optionally handle the case where the dialog is canceled
                    MessageBox.Show("No Path Selected.", "Selection Canceled", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void BrowseGeoJsonFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "GeoJSON files (*.geojson)|*.geojson|All files (*.*)|*.*",
                Title = "Select a GeoJSON File"
            };

            // Check if the dialog was opened and if a file was selected
            if (dialog.ShowDialog() == true)
            {
                // Check if the file has the correct extension
                string selectedFile = dialog.FileName;
                string extension = System.IO.Path.GetExtension(selectedFile).ToLower();

                if (extension == ".geojson")
                {
                    // Set the text box with the selected file path
                    GeoJsonFilePathTextBox.Text = selectedFile;
                }
                else
                {
                    MessageBox.Show("The selected file is not a GeoJSON file.", "Invalid File", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                // Optionally handle the case where the dialog is canceled
                MessageBox.Show("No file was selected.", "Selection Canceled", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        private void UpdateProgressBar(int totalDownloads , int completedDownloads)
        {
            if (totalDownloads > 0)
            {
                // Calculate the progress percentage
                double progressPercentage = (double)completedDownloads / totalDownloads * 100;

                // Update the progress bar value
                DownloadProgressBar.Value = progressPercentage;
                DownloadProgressText.Text = $"{(Int32)progressPercentage}%";
            }
        }

        private void DownloadWithUpdate(object sender, RoutedEventArgs e)
        {
            Update = true;
        }
        private void DownloadWithCheck(object sender, RoutedEventArgs e)
        {
            Update = false;
        }




        // we have two conditions *if user want to download tiles again then we will use DownloadFromBeginning()  
        // other condition that he want to check if (zoom level ) exist skipe that and download the levels which not exist==> DownloadAndCheck()

        private async Task DownloadFromBeginning(string FilePath, int MinZoom, int MaxZoom, string LocationToSave)
        {


            // Read File
            string filePath = FilePath;
            // Get direction to save tiles
            var SaveFiles = @$"{LocationToSave}/";
            //string baseUrl = "https://a.tile.openstreetmap.org/";

            // Read and parse the GeoJSON file
            string geoJsonContent = System.IO.File.ReadAllText(filePath);
            var geoJsonObject = JObject.Parse(geoJsonContent);

            
            var features = geoJsonObject["features"] as JArray;
            if (features == null)
            {
                Console.WriteLine("No features found in GeoJSON.");

            }

            var TotlaNumberOfDownloads = features.Count;
            var CompletedDownlod = 0;

            // Store tile coordinates to avoid duplicate downloads
            var tileCoordinates = new HashSet<(int X, int Y)>();
            string[] subdomains = { "a", "b", "c" }; //--->servers
            int subdomainIndex = 0;

            foreach (var feature in features)
            {
                var _locationName = feature["properties"]["orgid"];
                var _locationPath = $"{SaveFiles}{_locationName}";


                if (Directory.Exists(_locationPath))
                {
                    Directory.Delete(_locationPath, true);
                }


                for (var zoom = MinZoom; zoom <= MaxZoom; zoom++)
                {
                    
                   

                    
                    var new_path = $"{SaveFiles}{_locationName}/{zoom}";
                    


                    var geometry = feature["geometry"] as JObject;
                    if (geometry == null)
                    {
                        Console.WriteLine("Invalid geometry in feature.");
                        continue;
                    }

                    string geometryType = geometry["type"]?.ToString();
                    var coordinates = geometry["coordinates"] as JArray;

                    if (geometryType == "MultiPolygon" && coordinates != null)
                    {
                        foreach (var polygon in coordinates)
                        {
                            foreach (var ring in polygon)
                            {
                                var ringCoordinates = ring as JArray;
                                if (ringCoordinates != null)
                                {
                                    // Process each coordinate ring
                                    foreach (var point in ringCoordinates)
                                    {
                                        var latLonPair = point as JArray;
                                        if (latLonPair != null && latLonPair.Count == 2)
                                        {
                                            double lon = latLonPair[0].Value<double>();
                                            double lat = latLonPair[1].Value<double>();

                                            // Convert lat/lon to tile coordinates and add to set
                                            var tile = LatLonToTile(lat, lon, zoom); // Zoom level 15 as example
                                            tileCoordinates.Add(tile);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    // Download OSM tiles based on collected tile coordinates
                    foreach (var tile in tileCoordinates)
                    {

                        string subdomain = subdomains[subdomainIndex];
                        string url = $"https://{subdomain}.tile.openstreetmap.org/{zoom}/{tile.X}/{tile.Y}.png"; // Example URL for zoom level 15

                        new_path += $"/{tile.X}/{tile.Y}.png";

                        using (var response = await _httpClient.GetAsync(url))
                        {
                            if (response.IsSuccessStatusCode)
                            {
                                byte[] data = await response.Content.ReadAsByteArrayAsync();
                                Directory.CreateDirectory(Path.GetDirectoryName(new_path)); // Ensure directory exists
                                System.IO.File.WriteAllBytes(new_path, data);
                                Console.WriteLine($"Downloaded tile {tile.X}_{tile.Y} to {new_path}");
                            }
                            else
                            {
                                Console.WriteLine($"Failed to download tile {tile.X}_{tile.Y}");
                            }
                        }
                        new_path = $"{SaveFiles}{_locationName}/{zoom}";
                        await Task.Delay(5);
                        subdomainIndex = (subdomainIndex + 1) % subdomains.Length;
                    }
                    tileCoordinates = new HashSet<(int X, int Y)>();

                }


                UpdateProgressBar(TotlaNumberOfDownloads, ++CompletedDownlod);

            }


        }

        private async Task DownloadAndCheck(string FilePath, int MinZoom, int MaxZoom, string LocationToSave)
        {

            // Read File
            string filePath = FilePath;
            // Get direction to save tiles
            var SaveFiles = @$"{LocationToSave}/";
            //string baseUrl = "https://a.tile.openstreetmap.org/";

            // Read and parse the GeoJSON file
            string geoJsonContent = System.IO.File.ReadAllText(filePath);
            var geoJsonObject = JObject.Parse(geoJsonContent);



            var features = geoJsonObject["features"] as JArray;
            if (features == null)
            {
                Console.WriteLine("No features found in GeoJSON.");

            }

            var TotlaNumberOfDownloads = features.Count;
            var CompletedDownlod = 0;

            // Store tile coordinates to avoid duplicate downloads
            var tileCoordinates = new HashSet<(int X, int Y)>();
            string[] subdomains = { "a", "b", "c" }; //--->servers
            int subdomainIndex = 0;

            foreach (var feature in features)
            {

                for (var zoom = MinZoom; zoom <= MaxZoom; zoom++)
                {
                    var _locationName = feature["properties"]["orgid"];
                    var new_path = $"{SaveFiles}{_locationName}/{zoom}";

                    //here we will check if zoom level is exist or not 
                    if (Directory.Exists(new_path)) 
                    {
                        continue;
                    }
                    else
                    {
                        var geometry = feature["geometry"] as JObject;
                        if (geometry == null)
                        {
                            Console.WriteLine("Invalid geometry in feature.");
                            continue;
                        }

                        string geometryType = geometry["type"]?.ToString();
                        var coordinates = geometry["coordinates"] as JArray;

                        if (geometryType == "MultiPolygon" && coordinates != null)
                        {
                            foreach (var polygon in coordinates)
                            {
                                foreach (var ring in polygon)
                                {
                                    var ringCoordinates = ring as JArray;
                                    if (ringCoordinates != null)
                                    {
                                        // Process each coordinate ring
                                        foreach (var point in ringCoordinates)
                                        {
                                            var latLonPair = point as JArray;
                                            if (latLonPair != null && latLonPair.Count == 2)
                                            {
                                                double lon = latLonPair[0].Value<double>();
                                                double lat = latLonPair[1].Value<double>();

                                                // Convert lat/lon to tile coordinates and add to set
                                                var tile = LatLonToTile(lat, lon, zoom); // Zoom level 15 as example
                                                tileCoordinates.Add(tile);
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        // Download OSM tiles based on collected tile coordinates
                        foreach (var tile in tileCoordinates)
                        {

                            string subdomain = subdomains[subdomainIndex];
                            string url = $"https://{subdomain}.tile.openstreetmap.org/{zoom}/{tile.X}/{tile.Y}.png"; // Example URL for zoom level 15

                            new_path += $"/{tile.X}/{tile.Y}.png";

                            using (var response = await _httpClient.GetAsync(url))
                            {
                                if (response.IsSuccessStatusCode)
                                {
                                    byte[] data = await response.Content.ReadAsByteArrayAsync();
                                    Directory.CreateDirectory(Path.GetDirectoryName(new_path)); // Ensure directory exists
                                    System.IO.File.WriteAllBytes(new_path, data);
                                    Console.WriteLine($"Downloaded tile {tile.X}_{tile.Y} to {new_path}");
                                }
                                else
                                {
                                    Console.WriteLine($"Failed to download tile {tile.X}_{tile.Y}");
                                }
                            }
                            new_path = $"{SaveFiles}{_locationName}/{zoom}";
                            await Task.Delay(5);
                            subdomainIndex = (subdomainIndex + 1) % subdomains.Length;
                        }
                        tileCoordinates = new HashSet<(int X, int Y)>();
                    }

                   
                   

                }


                UpdateProgressBar(TotlaNumberOfDownloads, ++CompletedDownlod);

            }

        }



        // create Button Function to call our function to get Tiles from OSM.
        private async void DownloadButton_Click(object sender, EventArgs e)
        {
            // Call the method to get tiles
            if (MinZoomComboBox.SelectedItem is ComboBoxItem minItem &&
            MaxZoomComboBox.SelectedItem is ComboBoxItem maxItem)
            {
                int minZoom = int.Parse(minItem.Content.ToString());
                int maxZoom = int.Parse(maxItem.Content.ToString());

                // Call your function with the selected values
                await GetResultFromOSM(GeoJsonFilePathTextBox.Text ,minZoom , maxZoom  , OutputPathTextBox.Text);
            }
            else { Console.WriteLine("you have to Enter the values.."); }
            
        }
        private async Task GetResultFromOSM(string FilePath , int MinZoom , int MaxZoom, string LocationToSave)
        {
            if (Update)
            {
                DownloadFromBeginning(FilePath, MinZoom, MaxZoom ,LocationToSave);
            }
            else 
            {
                DownloadAndCheck(FilePath, MinZoom, MaxZoom, LocationToSave);
            }

        }
        private static (int X, int Y) LatLonToTile(double lat, double lon, int zoom)
        {
            // Convert latitude/longitude to tile coordinates
            double n = Math.Pow(2.0, zoom);
            double xtile = (lon + 180.0) / 360.0 * n;
            double ytile = (1.0 - Math.Log(Math.Tan(lat * Math.PI / 180.0) + 1.0 / Math.Cos(lat * Math.PI / 180.0)) / Math.PI) / 2.0 * n;
            return ((int)xtile, (int)ytile);
        }

    }
}
