// Default URL for triggering event grid function in the local environment.
// http://localhost:7071/runtime/webhooks/EventGrid?functionName={functionname}

// Learn how to locally debug an Event Grid-triggered function:
//    https://aka.ms/AA30pjh

// Use for local testing:
//   https://{ID}.ngrok.io/runtime/webhooks/EventGrid?functionName=Thumbnail

using Azure.Storage.Blobs;
using Microsoft.Azure.EventGrid.Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ImageFunctions
{
    public static class Thumbnail
    {
        private static readonly string BLOB_STORAGE_CONNECTION_STRING = Environment.GetEnvironmentVariable("AzureWebJobsStorage");

        private static string GetBlobNameFromUrl(string bloblUrl)
        {
            var uri = new Uri(bloblUrl);
            var blobClient = new BlobClient(uri);
            return blobClient.Name;
        }

        private static string GetBlobContainerNameFromUrl(string bloblUrl)
        {
            var uri = new Uri(bloblUrl);
            var blobClient = new BlobClient(uri);
            return blobClient.BlobContainerName;
        }

        private static IImageEncoder GetEncoder(string extension)
        {
            IImageEncoder encoder = null;

            extension = extension.Replace(".", "");

            var isSupported = Regex.IsMatch(extension, "gif|png|jpe?g", RegexOptions.IgnoreCase);

            if (isSupported)
            {
                switch (extension.ToLower())
                {
                    case "png":
                        encoder = new PngEncoder();
                        break;
                    case "jpg":
                        encoder = new JpegEncoder();
                        break;
                    case "jpeg":
                        encoder = new JpegEncoder();
                        break;
                    case "gif":
                        encoder = new GifEncoder();
                        break;
                    default:
                        break;
                }
            }

            return encoder;
        }

        [FunctionName("Thumbnail")]
        public static async Task Run(
            [EventGridTrigger]EventGridEvent eventGridEvent,
            [Blob("{data.url}", FileAccess.Read)] Stream input,
            ILogger log)
        {
            try
            {
                if (input != null)
                {
                    var createdEvent = ((JObject)eventGridEvent.Data).ToObject<StorageBlobCreatedEventData>();
                    var extension = Path.GetExtension(createdEvent.Url);
                    var encoder = GetEncoder(extension);

                    if (encoder != null)
                    {
                        var thumbnailWidth = Convert.ToInt32(Environment.GetEnvironmentVariable("THUMBNAIL_WIDTH"));
                        var blobName = GetBlobNameFromUrl(createdEvent.Url);
                        var containerName = GetBlobContainerNameFromUrl(createdEvent.Url);
                        var thumbName = CreateThumbnailName(blobName);
                        var blobServiceClient = new BlobServiceClient(BLOB_STORAGE_CONNECTION_STRING);
                        var blobContainerClient = blobServiceClient.GetBlobContainerClient(containerName);
                        blobContainerClient.CreateIfNotExists();

                        using (var output = new MemoryStream())
                        using (Image<Rgba32> image = Image.Load(input))
                        {
                            log.LogInformation("containerName: {code}", containerName);
                            log.LogInformation("Blob Name: {blobName}", blobName);
                            log.LogInformation("Thumb Name: {thumbName}", thumbName);
                            log.LogInformation("Thumbnail Width: {thumbnailWidth}", thumbnailWidth);
                            log.LogInformation("Image Height: {image.Height}", image.Height);
                            log.LogInformation("Image Width: {image.Width}", image.Width);

                            var divisor = image.Width / thumbnailWidth;

                            log.LogInformation($"Divisor: {divisor}");
                            
                            var height = Convert.ToInt32(Math.Round((decimal)(image.Height / divisor)));

                            log.LogInformation("New Height: {height}", height);
                            log.LogInformation("New Width: {thumbnailWidth}", thumbnailWidth);

                            image.Mutate(x => x.Resize(thumbnailWidth, height));
                            image.Save(output, encoder);
                            output.Position = 0;
                            await blobContainerClient.UploadBlobAsync(thumbName, output);
                        }
                    }
                    else
                    {
                        log.LogInformation("No encoder support for: {createdEvent.Url}", createdEvent.Url);
                    }
                }
            }
            catch (Exception ex)
            {
                log.LogInformation(ex.Message);
                throw;
            }
        }

        private static string CreateThumbnailName(string blobName)
        {
            return "thumb_" + blobName;
        }

        public static string GetCodeFromFullFileName(string fullfilename)
        {
            if (fullfilename.Contains("_"))
            {
                int indexSplit = fullfilename.IndexOf('_');
                return fullfilename.Substring(0, indexSplit);
            }

            throw new ArgumentException("Fullfilename does not fullfil requirements");
        }

        public static string GetFilenameFromFullFileName(string fullfilename)
        {
            if (fullfilename.Contains("_"))
            {
                int indexSplit = fullfilename.IndexOf('_');
                return fullfilename.Substring(indexSplit + 1);
            }

            throw new ArgumentException("Fullfilename does not fullfil requirements");
        }

    }
}
