const endpointUrl = process.env.DOCUMENT_UPLOAD_CONFIRM_ENDPOINT;
const internalApiKey = process.env.INTERNAL_API_KEY;

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

function extractS3Records(event) {
  if (Array.isArray(event.Records) && event.Records.some(record => record.s3)) {
    return event.Records;
  }

  if (Array.isArray(event.Records) && event.Records.some(record => record.eventSource === 'aws:sqs')) {
    return event.Records.flatMap(record => {
      const body = JSON.parse(record.body);
      return extractS3Records(body);
    });
  }

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
    const result = await postObjectCreated(bucketName, object.key, object.eTag, object.size);
    results.push(result);
  }

  return { processed: results.length, results };
};
