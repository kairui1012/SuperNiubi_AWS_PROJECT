# S3 Document Upload Confirmation Lambda

This Lambda belongs to the direct S3 document upload flow only. It lives in `S3-document-upload-confirmation-serverless` and is separate from the Stripe .NET Lambda project in `MyMvcApp.Serverless`.

## Runtime

- Node.js 20.x or newer
- Handler: `index.handler`

## Environment Variables

```text
DOCUMENT_UPLOAD_CONFIRM_ENDPOINT=https://your-domain.com/api/document-uploads/s3-object-created
INTERNAL_API_KEY=same-value-as-MVC-internal-api-key
```

If MVC uses Secrets Manager, the default secret is `prod/mymvcapp/secrets` with JSON field `InternalApi__Key`. `INTERNAL_API_KEY` must match that field value.

## Event Sources

The handler accepts:

- Direct S3 `ObjectCreated` notifications
- SQS messages wrapping S3 notifications
- EventBridge S3 object events

## Local Syntax Check

```bash
npm run check
```
