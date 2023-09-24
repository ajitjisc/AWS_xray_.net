using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.Lambda.S3Events;
using Amazon.StepFunctions.Model;
using Newtonsoft.Json;
using Amazon.XRay.Recorder.Core;
using Amazon.XRay.Recorder.Handlers.AwsSdk;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace CheckCsvColumns
{
    
    public class Function
    {
        
        private readonly AmazonS3Client _s3Client;
        private readonly Amazon.StepFunctions.IAmazonStepFunctions _stepFunctionsClient;

        public Function()
        {
            AWSXRayRecorder.InitializeInstance();
            AWSSDKHandler.RegisterXRayForAllServices(); 
            _s3Client = new AmazonS3Client();
            _stepFunctionsClient = new Amazon.StepFunctions.AmazonStepFunctionsClient();
        }

        public async Task FunctionHandler(S3Event evnt, ILambdaContext context)
        {
            context.Logger.LogLine($"Received event: {JsonConvert.SerializeObject(evnt)}");

            context.Logger.LogLine("new_testing_1");
            

            if (evnt == null) 
            {
                context.Logger.LogLine("Event is null.");
                return;
            }
            if (evnt.Records == null) 
            {
                context.Logger.LogLine("Records in the event is null.");
                return;
            }

            // Check for null event or null records
            if (!evnt.Records.Any()) 
            {
                context.Logger.LogLine($"Event has no records. Event: {JsonConvert.SerializeObject(evnt)}");
                return;
            }


            foreach (var record in evnt.Records)
            {
                
                if (record == null) 
                {
                    context.Logger.LogLine("Record is null");
                    continue;
                }
                if (record.S3 == null) 
                {
                    context.Logger.LogLine("S3 in the record is null");
                    continue;
                }
                if (record.S3.Bucket == null) 
                {
                    context.Logger.LogLine("Bucket in the S3 record is null");
                    continue;
                }
                if (record.S3.Bucket.Name == null) 
                {
                    context.Logger.LogLine("Bucket name in the S3 record is null");
                    continue;
                }
                if (record.S3.Object == null) 
                {
                    context.Logger.LogLine("Object in the S3 record is null");
                    continue;
                }

                string bucket = record.S3.Bucket.Name;
                string key = record.S3.Object.Key;
                

                if (bucket != "ddpsourcebucket")
                {
                    context.Logger.LogLine($"Ignoring event from bucket {bucket}.");
                    continue;
                }

                if(string.IsNullOrEmpty(bucket) || string.IsNullOrEmpty(key))
                {
                    context.Logger.LogLine("Bucket or Key value is missing in the S3 event.");
                    return;
                }
                
                using (var response = await _s3Client.GetObjectAsync(bucket, key))
                using (Stream responseStream = response.ResponseStream)
                using (StreamReader reader = new StreamReader(responseStream))
                {
                    string content = reader.ReadToEnd();
                    var lines = content.Split('\n');
                    var columns = lines[0].Split(',');
                    AWSXRayRecorder.Instance.BeginSubsegment("CSV Validation"); // Start the CSV Validation subsegment

                    if (columns.Length != 3)
                    {
                        AWSXRayRecorder.Instance.AddMetadata("Error", "Invalid CSV format");  // Adding metadata to subsegment
                        AWSXRayRecorder.Instance.MarkFault();  // Mark the segment/subsegment as faulty
                        AWSXRayRecorder.Instance.EndSubsegment(); // Close the CSV Validation subsegment
                        throw new Exception("Invalid CSV file, required columns missing.");
                    }

                    AWSXRayRecorder.Instance.EndSubsegment();  // Close the CSV Validation subsegment when it's successful

                    context.Logger.LogLine("Test Valid CSV file.");

                    var stepFunctionArn = GetStateMachineArn("sfn_state_machine", context);  // Passing context to GetStateMachineArn

                    if(string.IsNullOrEmpty(stepFunctionArn))
                    {
                        context.Logger.LogLine($"StateMachine named 'sfn_state_machine' not found.");
                        return;
                    }

                    var startExecutionRequest = new StartExecutionRequest
                    {
                        StateMachineArn = stepFunctionArn,
                        Input = $"{{\"bucket\":\"{bucket}\", \"key\":\"{key}\"}}"
                    };
                    try
                    {
                        await _stepFunctionsClient.StartExecutionAsync(startExecutionRequest);
                    }
                    catch (Exception ex)
                    {
                        context.Logger.LogLine($"Error starting the Step Functions execution: {ex.Message}");
                        throw;
                    }
                }
            }
        }

        private string GetStateMachineArn(string stateMachineName, ILambdaContext context)  // Accepting ILambdaContext as a parameter
        {
            try
            {
                var listRequest = new Amazon.StepFunctions.Model.ListStateMachinesRequest();  // Creating a new request object
                var listResponse = _stepFunctionsClient.ListStateMachinesAsync(listRequest).Result;
                var stateMachine = listResponse.StateMachines.FirstOrDefault(sm => sm.Name == stateMachineName);
                
                if (stateMachine == null)
                {
                    context.Logger.LogLine($"StateMachine named '{stateMachineName}' not found.");
                    return null;
                }
                
                return stateMachine.StateMachineArn;
            }
            catch (Exception ex)
            {
                context.Logger.LogLine($"Error fetching state machine ARN: {ex.Message}");
                throw;
            }
        }
    }
}
