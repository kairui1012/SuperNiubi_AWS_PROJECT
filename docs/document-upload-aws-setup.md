# Document Upload AWS Setup

This implementation keeps MVC as the authenticated upload broker because the app currently uses MVC cookies for Tenant/Landlord sessions. The heavy file transfer is direct browser-to-S3, and S3 ObjectCreated events confirm the DB record asynchronously.

## Required Application Settings

Set these in production configuration or secrets:

```json
{
  "AWS": {
    "BucketName": "your-bucket",
    "Region": "ap-southeast-1",
    "UploadUrlExpiryMinutes": 15
  },
  "InternalApi": {
    "Key": "generate-a-long-random-secret",
    "SecretId": "optional-secrets-manager-secret-name-or-arn"
  }
}
```

For production, prefer storing the shared key in Secrets Manager and setting:

```text
InternalApi__SecretId=your-secret-name-or-arn
```

The secret value may be either the raw key string or a JSON object with one of these fields:

```json
{
  "INTERNAL_API_KEY": "same-long-random-secret"
}
```

Nested JSON also works:

```json
{
  "InternalApi": {
    "Key": "same-long-random-secret"
  }
}
```

The MVC app resolves the internal API key in this order:

1. `InternalApi:Key`
2. Secrets Manager value from `InternalApi:SecretId`
3. legacy `EventBridge:SharedSecret`

The same resolved key must be set as the Lambda environment variable `INTERNAL_API_KEY`.

## S3 CORS

Configure the document bucket CORS so browsers can PUT to the presigned URL:

```json
[
  {
    "AllowedOrigins": ["https://your-domain.com"],
    "AllowedMethods": ["PUT", "GET"],
    "AllowedHeaders": ["*"],
    "ExposeHeaders": ["ETag"],
    "MaxAgeSeconds": 3000
  }
]
```

For local development, temporarily add your localhost origin.

## S3 Event Flow

Recommended production wiring:

```text
S3 ObjectCreated
  -> SQS queue
  -> Lambda S3-document-upload-confirmation-serverless/index.mjs
  -> POST /api/document-uploads/s3-object-created
```

This Lambda is separate from the Stripe `.NET` Lambda project in `MyMvcApp.Serverless`.

Lambda environment variables:

```text
DOCUMENT_UPLOAD_CONFIRM_ENDPOINT=https://your-domain.com/api/document-uploads/s3-object-created
INTERNAL_API_KEY=same-value-as-MVC-internal-api-key
```

If MVC uses Secrets Manager, `INTERNAL_API_KEY` must match the value stored in that secret.

The Lambda can consume direct S3 notification events, SQS-wrapped S3 events, or EventBridge S3 object events.

Use a Node.js 20.x or newer Lambda runtime. The handler is:

```text
index.handler
```

## IAM

The MVC app role needs:

```text
s3:PutObject
s3:GetObject
s3:GetObjectMetadata
secretsmanager:GetSecretValue
```

Scope those permissions to the document prefixes:

```text
tenant/*/documents/*
landlord/*/documents/*
```

If using SQS between S3 and Lambda, the Lambda role also needs `sqs:ReceiveMessage`, `sqs:DeleteMessage`, and `sqs:GetQueueAttributes`.

## Database

Apply the EF migration:

```bash
dotnet ef database update --project MyMvcApp/MyMvcApp.csproj
```

Existing documents are migrated as `Confirmed`; new direct uploads start as `PendingUpload` and become `Confirmed` only after the S3 event validation succeeds.
