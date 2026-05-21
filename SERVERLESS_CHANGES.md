# Serverless Stripe Worker Changes

## What Changed

The main ASP.NET Core MVC app is still the normal web application and still targets the existing EC2/Docker deployment.

Only Stripe asynchronous payment event processing was moved into a new AWS Lambda worker:

```text
Stripe -> Amazon EventBridge -> AWS Lambda -> PostgreSQL / S3 / SES
Users  -> ASP.NET Core MVC app -> EC2 / Docker
```

This is not a full Lambda migration.

## How To Prove It Is Serverless

This project is partially serverless.

The main web application is not serverless. It remains:

```text
User browser -> ASP.NET Core MVC app -> Docker / EC2
```

The Stripe payment event processor is serverless:

```text
Stripe -> Amazon EventBridge partner event bus -> AWS Lambda -> PostgreSQL / S3 / SES
```

Code evidence:

- `MyMvcApp.Serverless/MyMvcApp.Serverless.csproj` contains:

```xml
<AWSProjectType>Lambda</AWSProjectType>
```

- `MyMvcApp.Serverless/Function.cs` contains the Lambda handler:

```csharp
public async Task<StripeEventLambdaResponse> FunctionHandler(JsonElement payload, ILambdaContext context)
```

- The Lambda project references AWS Lambda packages:

```xml
<PackageReference Include="Amazon.Lambda.Core" Version="2.8.0" />
<PackageReference Include="Amazon.Lambda.Serialization.SystemTextJson" Version="2.4.4" />
```

AWS evidence after deployment:

1. Lambda Console screenshot
   - Runtime: `.NET 8`
   - Handler: `MyMvcApp.Serverless::MyMvcApp.Serverless.Function::FunctionHandler`
2. EventBridge screenshot
   - Event bus is the Stripe partner event bus.
   - Rule target is the Lambda function.
3. Stripe Dashboard screenshot
   - Event destination type is Amazon EventBridge.
   - Destination is enabled.
4. CloudWatch Logs screenshot
   - Lambda log shows a processed Stripe event.
5. Database screenshot/query
   - Payment status changes after Stripe sends the event.

Suggested explanation for report or presentation:

```text
The project is not fully serverless. The main ASP.NET Core MVC application remains deployed on EC2 using Docker. The serverless component is the Stripe payment event processor, implemented as an AWS Lambda function triggered by Amazon EventBridge partner events from Stripe.
```

## Files Added

- `MyMvcApp.Serverless/MyMvcApp.Serverless.csproj`
  - New .NET 8 Lambda project.
- `MyMvcApp.Serverless/Function.cs`
  - Lambda entry point.
  - Handler: `MyMvcApp.Serverless::MyMvcApp.Serverless.Function::FunctionHandler`
- `MyMvcApp/Services/StripeEventBridgeProcessingService.cs`
  - Shared Stripe/EventBridge processing service used by both MVC and Lambda.
- `SERVERLESS_CHANGES.md`
  - This summary.

## Files Modified

- `MyMvcApp/Controllers/StripeEventBridgeController.cs`
  - Simplified to HTTP authorization, X-Ray logging, and result conversion.
  - Business logic was moved to `StripeEventBridgeProcessingService`.
- `MyMvcApp/Program.cs`
  - Added dependency injection registration:

```csharp
builder.Services.AddScoped<StripeEventBridgeProcessingService>();
```

- `dotNET.sln`
  - Added `MyMvcApp.Serverless` to the solution.

## Events Covered

The Lambda/shared service handles:

- `checkout.session.completed`
- `checkout.session.async_payment_failed`
- `checkout.session.expired`
- `payment_intent.succeeded`
- `payment_intent.payment_failed`
- `charge.refunded`
- `refund.created`
- `refund.updated`

## Deploy Without SAM

Publish and zip the Lambda:

```bash
dotnet publish MyMvcApp.Serverless/MyMvcApp.Serverless.csproj -c Release -o ./publish-lambda
cd publish-lambda
zip -r ../propease-stripe-lambda.zip .
```

Create or update the Lambda in AWS Console:

- Runtime: `.NET 8`
- Handler: `MyMvcApp.Serverless::MyMvcApp.Serverless.Function::FunctionHandler`

Set Lambda environment variables:

```text
ASPNETCORE_ENVIRONMENT=Production
AWS__Region=ap-southeast-1
AWS__BucketName=<your-s3-bucket>
AWS__SesSenderEmail=<your-verified-ses-email>
ConnectionStrings__DefaultConnection=<your-postgresql-connection-string>
Stripe__SecretKey=<your-stripe-secret-key>
```

Set Lambda role permissions:

- CloudWatch Logs
- X-Ray write
- S3 `PutObject` / `GetObject`
- SES `SendEmail` / `SendRawEmail`

Then in AWS EventBridge:

1. Associate the pending Stripe partner event source with an event bus.
2. Create a rule on the Stripe partner event bus, not the default bus.
3. Use the Lambda function as the target.

If the database is private inside a VPC, configure Lambda VPC access and allow Lambda security group traffic to PostgreSQL port `5432`.

## Verification Run

```bash
dotnet restore MyMvcApp.Serverless/MyMvcApp.Serverless.csproj -v minimal
dotnet build dotNET.sln --no-restore -v minimal --disable-build-servers /m:1
dotnet build dotNET.sln -c Release --no-restore -v minimal --disable-build-servers /m:1
dotnet publish MyMvcApp.Serverless/MyMvcApp.Serverless.csproj -c Release --no-restore -o /private/tmp/propease-lambda-publish
git diff --check
```

Builds passed. Existing nullable warnings in the MVC project are unrelated to this serverless change.
