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
    "Key": "generate-a-long-random-secret"
  }
}
```

The same `InternalApi:Key` value must be set as the Lambda environment variable `INTERNAL_API_KEY`.

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
  -> Lambda aws/lambdas/s3-document-upload-confirmation/index.mjs
  -> POST /api/document-uploads/s3-object-created
```

Lambda environment variables:

```text
DOCUMENT_UPLOAD_CONFIRM_ENDPOINT=https://your-domain.com/api/document-uploads/s3-object-created
INTERNAL_API_KEY=same-value-as-InternalApi__Key
```

The Lambda can consume direct S3 notification events, SQS-wrapped S3 events, or EventBridge S3 object events.

## IAM

The MVC app role needs:

```text
s3:PutObject
s3:GetObject
s3:GetObjectMetadata
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
