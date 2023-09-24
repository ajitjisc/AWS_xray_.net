using System;
using System.IO;
using Amazon.Lambda.Core;
using Amazon.S3;
using Amazon.S3.Model;
using Newtonsoft.Json;
using Amazon.XRay.Recorder.Core;
using Amazon.XRay.Recorder.Handlers.AwsSdk;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace ConvertTsvToCsv
{
    public class Function
    {
        private readonly AmazonS3Client _s3Client;
        public Function()
        {
            AWSXRayRecorder.InitializeInstance();
            AWSSDKHandler.RegisterXRayForAllServices(); 
            _s3Client = new AmazonS3Client();
        }

        public class StepFunctionInput
        {
            public string SourceBucket { get; set; }
            public string SourceKey { get; set; }
        }

        public async Task FunctionHandler(StepFunctionInput input, ILambdaContext context)
        {
            context.Logger.LogLine($"Received input: {JsonConvert.SerializeObject(input)}");

            string sourceBucket = input.SourceBucket;
            string sourceKey = input.SourceKey;

            using (var response = await _s3Client.GetObjectAsync(sourceBucket, sourceKey))
            using (Stream responseStream = response.ResponseStream)
            using (StreamReader reader = new StreamReader(responseStream))
            {
                string content = reader.ReadToEnd();
                string convertedContent = content.Replace(',', '\t');

                string destinationBucket = "ddpdestinationbucket"; // Replace with your bucket name
                string destinationKey = sourceKey.Replace(".csv", ".tsv");
                await PutObjectToS3Async(destinationBucket, destinationKey, convertedContent);
                context.Logger.LogLine($"File successfully converted and saved to {destinationBucket}/{destinationKey}");
            }
        }

        private async Task PutObjectToS3Async(string bucket, string key, string content)
        {
            var putRequest = new PutObjectRequest
            {
                BucketName = bucket,
                Key = key,
                ContentBody = content
            };

            await _s3Client.PutObjectAsync(putRequest);
        }
    }
}
