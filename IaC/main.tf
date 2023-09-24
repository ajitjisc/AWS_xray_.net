provider "aws" {
  region  = "eu-west-1"
  profile = "da-dev"
}

resource "aws_s3_bucket" "source_bucket" {
  bucket = "ddpsourcebucket"
}

resource "aws_s3_bucket" "destination_bucket" {
  bucket = "ddpdestinationbucket"
}

resource "aws_iam_role" "ddp_lambda_1" {
  name = "ddp_lambda_1"

  assume_role_policy = jsonencode({
    Version = "2012-10-17",
    Statement = [
      {
        Action = "sts:AssumeRole",
        Principal = {
          Service = ["lambda.amazonaws.com", "states.amazonaws.com"]
        },
        Effect = "Allow",
      },
    ]
  })
}

resource "aws_iam_role_policy_attachment" "lambda_logs" {
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole"
  role       = aws_iam_role.ddp_lambda_1.name
}

resource "aws_iam_policy" "lambda_s3_access" {
  name        = "LambdaS3AccessPolicy"
  description = "Allow Lambda to have full access to S3 buckets"

  policy = jsonencode({
    Version = "2012-10-17",
    Statement = [
      {
        Effect = "Allow",
        Action = "s3:*",
        Resource = "*"
      }
    ]
  })
}

resource "aws_iam_policy" "lambda_xray" {
  name        = "LambdaXRayAccessPolicy"
  description = "Allow Lambda to send traces to X-Ray"

  policy = jsonencode({
    Version = "2012-10-17",
    Statement = [
      {
        Effect = "Allow",
        Action = [
          "xray:PutTraceSegments",
          "xray:PutTelemetryRecords",
          "xray:GetSamplingRules",
          "xray:GetSamplingTargets",
          "xray:GetSamplingStatisticSummaries"
        ],
        Resource = "*"
      }
    ]
  })
}

resource "aws_iam_role_policy_attachment" "lambda_xray_attachment" {
  policy_arn = aws_iam_policy.lambda_xray.arn
  role       = aws_iam_role.ddp_lambda_1.name
}


resource "aws_iam_role_policy_attachment" "lambda_s3_access_attachment" {
  policy_arn = aws_iam_policy.lambda_s3_access.arn
  role       = aws_iam_role.ddp_lambda_1.name
}

resource "aws_lambda_function" "check_csv_columns" {
  filename      = "/Users/ajit/Desktop/AWS_xray_.net/lambda/CheckCsvColumns/CheckCsvColumns/src/CheckCsvColumns/CheckCsvColumns.zip"
  function_name = "CheckCsvColumns"
  role          = aws_iam_role.ddp_lambda_1.arn
  handler       = "CheckCsvColumns::CheckCsvColumns.Function::FunctionHandler"
  runtime       = "dotnet6"
  timeout       = 450
  tracing_config {
    mode = "Active"
  }
}

resource "aws_lambda_function" "convert_tsv_to_csv" {
  filename      = "/Users/ajit/Desktop/AWS_xray_.net/lambda/ConvertTsvToCsv/ConvertTsvToCsv/src/ConvertTsvToCsv/ConvertTsvToCsv.zip"
  function_name = "ConvertTsvToCsv"
  role          = aws_iam_role.ddp_lambda_1.arn
  handler       = "ConvertTsvToCsv::ConvertTsvToCsv.Function::FunctionHandler"
  runtime       = "dotnet6"
  timeout       = 450
  tracing_config {
    mode = "Active"
  }
}

resource "aws_iam_policy" "step_functions_invoke" {
  name        = "InvokeStepFunctionsPolicy"
  description = "Allow Lambda to start and list Step Functions executions"

  policy = jsonencode({
    Version = "2012-10-17",
    Statement = [
      {
        Effect = "Allow",
        Action = [
          "states:StartExecution",
          "states:ListStateMachines"
        ],
        Resource = "*"
      }
    ]
  })
}

resource "aws_iam_role_policy_attachment" "lambda_step_functions_invoke_attachment" {
  policy_arn = aws_iam_policy.step_functions_invoke.arn
  role       = aws_iam_role.ddp_lambda_1.name
}

resource "aws_iam_policy" "sfn_invoke_lambda" {
  name        = "SFNInvokeLambdaPolicy"
  description = "Allow Step Functions to invoke Lambda function"

  policy = jsonencode({
    Version = "2012-10-17",
    Statement = [
      {
        Effect = "Allow",
        Action = "lambda:InvokeFunction",
        Resource = [
          aws_lambda_function.check_csv_columns.arn,
          aws_lambda_function.convert_tsv_to_csv.arn
        ]
      }
    ]
  })
}

resource "aws_iam_role_policy_attachment" "sfn_invoke_lambda_attachment" {
  policy_arn = aws_iam_policy.sfn_invoke_lambda.arn
  role       = aws_iam_role.ddp_lambda_1.name
}

resource "aws_iam_policy" "lambda_s3_get_object" {
  name        = "LambdaS3GetObjectPolicy"
  description = "Allow Lambda to read objects from S3 buckets"

  policy = jsonencode({
    Version = "2012-10-17",
    Statement = [
      {
        Effect = "Allow",
        Action = "s3:GetObject",
        Resource = [
          aws_s3_bucket.source_bucket.arn,
          aws_s3_bucket.destination_bucket.arn
        ]
      }
    ]
  })
}

resource "aws_iam_role_policy_attachment" "lambda_s3_get_object_attachment" {
  policy_arn = aws_iam_policy.lambda_s3_get_object.arn
  role       = aws_iam_role.ddp_lambda_1.name
}

resource "aws_sfn_state_machine" "sfn_state_machine" {
  name     = "sfn_state_machine"
  role_arn = aws_iam_role.ddp_lambda_1.arn
  
  # Here's where we make the change. 
  # We hardcode values for demonstration, but in a real-world scenario, you'd probably
  # dynamically populate this data based on your needs.
  definition = jsonencode({
    StartAt = "ConvertTsvToCsv",
    States = {
      ConvertTsvToCsv = {
        Type     = "Task",
        Resource = aws_lambda_function.convert_tsv_to_csv.arn,
        Parameters = {
          "SourceBucket": "ddpsourcebucket",
          "SourceKey": "ddp_test.csv"
        },
        End      = true
      }
    }
  })
}


resource "aws_lambda_permission" "allow_bucket" {
  statement_id  = "AllowExecutionFromS3Bucket"
  action        = "lambda:InvokeFunction"
  function_name = aws_lambda_function.check_csv_columns.function_name
  principal     = "s3.amazonaws.com"
  source_arn    = aws_s3_bucket.source_bucket.arn
  source_account = data.aws_caller_identity.current.account_id
}

resource "aws_lambda_permission" "allow_bucket_convert" {
  statement_id  = "AllowExecutionFromS3BucketConvert"
  action        = "lambda:InvokeFunction"
  function_name = aws_lambda_function.convert_tsv_to_csv.function_name
  principal     = "s3.amazonaws.com"
  source_arn    = aws_s3_bucket.source_bucket.arn
  source_account = data.aws_caller_identity.current.account_id
}


data "aws_caller_identity" "current" {}

resource "aws_s3_bucket_notification" "s3_notification_check" {
  bucket = aws_s3_bucket.source_bucket.bucket

  lambda_function {
    lambda_function_arn = aws_lambda_function.check_csv_columns.arn
    events              = ["s3:ObjectCreated:*"]
  }
}
