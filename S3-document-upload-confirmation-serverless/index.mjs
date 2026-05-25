// MVC endpoint that receives the upload confirmation callback.
// Example: https://your-domain.com/api/document-uploads/s3-object-created
const endpointUrl = process.env.DOCUMENT_UPLOAD_CONFIRM_ENDPOINT;

// Shared secret used to prove this callback came from the trusted Lambda workflow.
// The MVC app checks this value through the X-Internal-Api-Key header.
const internalApiKey = process.env.INTERNAL_API_KEY;

// Sends one confirmed S3 object-created notification to the MVC application.
// The Lambda does not update the database directly; MVC performs the final
// S3 metadata check and changes the document status from PendingUpload to Confirmed.
async function postObjectCreated(bucketName, key, eTag, size) {
  if (!endpointUrl || !internalApiKey) {
    throw new Error('DOCUMENT_UPLOAD_CONFIRM_ENDPOINT and INTERNAL_API_KEY must be configured.');
  }

  const response = await fetch(endpointUrl, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'X-Internal-Api-Key': internalApiKey
    },
    body: JSON.stringify({
      bucketName,
      // S3 event keys are URL-encoded and spaces may arrive as +, so decode before MVC lookup.
      key: decodeURIComponent(key.replace(/\+/g, ' ')),
      eTag,
      size
    })
  });

  if (!response.ok) {
    const text = await response.text();
    throw new Error(`MVC confirmation endpoint failed with ${response.status}: ${text}`);
  }

  return response.json();
}

// Normalizes supported AWS event shapes into a plain list of S3 records.
// This Lambda supports:
// 1. Direct S3 ObjectCreated event -> event.Records[].s3
// 2. SQS-triggered Lambda event -> event.Records[].body contains the original S3 event JSON
// 3. EventBridge S3 event -> event.detail.bucket / event.detail.object
function extractS3Records(event) {
  // Direct S3 notification shape. Lambda receives S3 records directly.
  if (Array.isArray(event.Records) && event.Records.some(record => record.s3)) {
    return event.Records;
  }

  // SQS notification shape. Lambda is triggered by SQS, and each SQS message body
  // contains the original S3 event as JSON. This code parses the body, then calls
  // extractS3Records again so the same S3 parsing logic can be reused.
  if (Array.isArray(event.Records) && event.Records.some(record => record.eventSource === 'aws:sqs')) {
    return event.Records.flatMap(record => {
      const body = JSON.parse(record.body);
      return extractS3Records(body);
    });
  }

  // EventBridge S3 event shape. EventBridge puts bucket/object information under detail.
  if (event.detail?.bucket?.name && event.detail?.object?.key) {
    return [{
      s3: {
        bucket: { name: event.detail.bucket.name },
        object: {
          key: event.detail.object.key,
          eTag: event.detail.object.etag,
          size: event.detail.object.size
        }
      }
    }];
  }

  return [];
}

// Lambda entry point. AWS calls this handler when the configured trigger fires.
// The trigger can be S3 directly, SQS, or EventBridge depending on deployment.
export const handler = async (event) => {
  const records = extractS3Records(event);

  if (records.length === 0) {
    console.log('No S3 records found in event.', JSON.stringify(event));
    return { processed: 0 };
  }

  const results = [];
  for (const record of records) {
    const bucketName = record.s3.bucket.name;
    const object = record.s3.object;

    // For each S3 object-created record, notify MVC so the application database
    // can confirm the pending document upload.
    const result = await postObjectCreated(bucketName, object.key, object.eTag, object.size);
    results.push(result);
  }

  return { processed: results.length, results };
};
