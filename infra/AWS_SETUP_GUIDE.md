# AWS Dev Deployment Setup Guide

## Prerequisites

This guide walks you through configuring AWS credentials and deploying the `zip02` application to AWS Dev.

---

## Step 1: Configure AWS CLI Credentials

### Option A: Using AWS CLI (Recommended)

1. **Install AWS CLI** if you haven't already:
   ```bash
   # Download from https://awscli.amazonaws.com/AWSCLIV2.msi
   # Or via Chocolatey:
   choco install awscli
   ```

2. **Configure credentials**:
   ```powershell
   aws configure
   ```
   When prompted, enter:
   - **AWS Access Key ID**: Your IAM user's access key
   - **AWS Secret Access Key**: Your IAM user's secret key
   - **Default region**: `eu-west-1` (or your preferred region)
   - **Default output format**: `json`

3. **Verify setup**:
   ```powershell
   aws sts get-caller-identity
   ```
   Should return your AWS account details.

### Option B: Using Environment Variables

```powershell
$env:AWS_ACCESS_KEY_ID = "your-access-key"
$env:AWS_SECRET_ACCESS_KEY = "your-secret-key"
$env:AWS_DEFAULT_REGION = "eu-west-1"
```

---

## Step 2: Verify AWS Toolkit Installation

1. In Visual Studio, check **Extensions → Manage Extensions** for "AWS Toolkit"
2. If not installed:
   - Go to **Extensions → Manage Extensions**
   - Search for "AWS Toolkit for Visual Studio 2022"
   - Install and restart Visual Studio

---

## Step 3: Configure SAM Parameters for Dev

Edit `infra/sam/parameters/dev.json` and ensure the following are set:

```json
[
  {
	"ParameterKey": "Env",
	"ParameterValue": "dev"
  },
  {
	"ParameterKey": "TableName",
	"ParameterValue": "Tickets-Dev"
  },
  {
	"ParameterKey": "StripeWebhookSecretSsmPath",
	"ParameterValue": "/zip02/dev/stripe-webhook-secret"
  },
  {
	"ParameterKey": "InternalSchedulerTokenSsmPath",
	"ParameterValue": "/zip02/dev/internal-scheduler-token"
  },
  {
	"ParameterKey": "CognitoClientId",
	"ParameterValue": ""
  },
  {
	"ParameterKey": "CognitoUserPoolId",
	"ParameterValue": ""
  },
  {
	"ParameterKey": "CognitoRegion",
	"ParameterValue": "eu-west-1"
  }
]
```

### Notes:
- Leave **CognitoClientId** and **CognitoUserPoolId** empty for now (public routes will work; organizer routes will require manual setup)
- For **StripeWebhookSecretSsmPath** and **InternalSchedulerTokenSsmPath**, you have two options:
  - Create the secrets in AWS SSM Parameter Store manually
  - Or deploy without them and add later

---

## Step 4: Create AWS SSM Parameter Store Secrets (Optional but Recommended)

```powershell
# Store the internal scheduler token (used by EventBridge)
aws ssm put-parameter `
  --name "/zip02/dev/internal-scheduler-token" `
  --value "your-random-secure-token-here" `
  --type "SecureString" `
  --region eu-west-1 `
  --overwrite

# Store Stripe webhook secret (if you have it)
aws ssm put-parameter `
  --name "/zip02/dev/stripe-webhook-secret" `
  --value "whsec_dev_xxxxxxxxxxxx" `
  --type "SecureString" `
  --region eu-west-1 `
  --overwrite
```

If these don't exist, the Lambda will fail to start. You can set placeholder values for now.

---

## Step 5: Deploy to AWS Dev

```powershell
cd infra/scripts

# Deploy to dev
.\deploy.ps1 -Environment dev
```

**What happens:**
1. Builds the SAM template (requires Docker for `--use-container`)
2. Creates a CloudFormation stack named `zip02-stack-dev`
3. Provisions:
   - DynamoDB table (`Tickets-Dev`)
   - Lambda function
   - API Gateway HTTP API
   - EventBridge rules (reservation expiry, reconciliation schedule)
   - IAM role and CloudWatch logs

**Expected output:**
```
✓ Building serverless application
✓ Deploying CloudFormation stack
✓ Creating resources...
✓ Stack outputs:
  ApiEndpoint: https://xxxxxxxx.execute-api.eu-west-1.amazonaws.com
```

---

## Step 6: Test the Deployment

### Get the API Endpoint

```powershell
aws cloudformation describe-stacks `
  --stack-name zip02-stack-dev `
  --query "Stacks[0].Outputs[?OutputKey=='ApiEndpoint'].OutputValue" `
  --region eu-west-1 `
  --output text
```

### Test a Public Route (no auth required)

```powershell
$apiEndpoint = "https://xxxxxxxx.execute-api.eu-west-1.amazonaws.com"

# Create an event
$event = @{
	Title = "Test Event"
	Capacity = 100
	StartAtUtc = (Get-Date).AddDays(1).ToUniversalTime()
	EndAtUtc = (Get-Date).AddDays(1).AddHours(2).ToUniversalTime()
} | ConvertTo-Json

curl -X POST "$apiEndpoint/events" `
  -H "Content-Type: application/json" `
  -d $event
```

### Test the OpenAPI/Swagger UI

```
https://xxxxxxxx.execute-api.eu-west-1.amazonaws.com/swagger/ui
```

(Note: Swagger is only available if `ASPNETCORE_ENVIRONMENT=Development` in Lambda env vars. For dev, it should be active.)

---

## Step 7: Monitor & Troubleshoot

### Check Lambda Logs

```powershell
# View recent logs
aws logs tail /aws/lambda/zip02-lambda-dev --follow --region eu-west-1
```

### Check CloudFormation Stack Status

```powershell
aws cloudformation describe-stacks `
  --stack-name zip02-stack-dev `
  --region eu-west-1 `
  --query "Stacks[0].[StackStatus, StackStatusReason]"
```

### Common Issues

| Issue | Solution |
|-------|----------|
| Access Denied | Check AWS credentials: `aws sts get-caller-identity` |
| SSM Parameter Not Found | Create the parameter in AWS SSM: `aws ssm put-parameter --name /zip02/dev/... --value ... --type SecureString` |
| Lambda Timeout | Increase timeout in `infra/sam/template.yaml` `Timeout: 60` |
| DynamoDB Throttling | Check DynamoDB provisioning in `infra/sam/template.yaml` |
| EventBridge Rule Not Triggering | Verify rule is `ENABLED` in console; check IAM role permissions |

---

## Step 8: Clean Up (When Done)

```powershell
cd infra/scripts

# Teardown the stack
.\teardown.ps1 -Environment dev
```

---

## Next Steps

1. **Set up Cognito** (for organizer authentication):
   - Create a Cognito User Pool in AWS
   - Create an App Client
   - Update `dev.json` parameters with the IDs

2. **Set up Stripe** (for payment webhooks):
   - Create a webhook endpoint in Stripe Dashboard
   - Point it to `https://<your-api>/webhooks/stripe`
   - Add the webhook secret to SSM

3. **Monitor & Alert**:
   - Set up CloudWatch alarms for Lambda errors
   - Monitor DynamoDB metrics
   - Track EventBridge rule execution

---

## Helpful Commands Reference

```powershell
# Deploy dev
.\deploy.ps1 -Environment dev

# Deploy test
.\deploy.ps1 -Environment test

# Teardown dev
.\teardown.ps1 -Environment dev -Force

# View stack outputs
aws cloudformation describe-stacks --stack-name zip02-stack-dev --query "Stacks[0].Outputs" --region eu-west-1

# List recent Lambda invocations
aws cloudwatch get-metric-statistics `
  --namespace AWS/Lambda `
  --metric-name Invocations `
  --dimensions Name=FunctionName,Value=zip02-lambda-dev `
  --start-time (Get-Date).AddHours(-1).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ") `
  --end-time (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ") `
  --period 300 `
  --statistics Sum `
  --region eu-west-1
```

---

**Ready to deploy? Start with Step 1!**
