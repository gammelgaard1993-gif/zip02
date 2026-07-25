# Environment Setup & Parameter Configuration Guide

## Overview

This guide walks through configuring and deploying the zip02 system across dev, test, and prod environments using AWS SAM.

## Prerequisites

1. **AWS Account** with appropriate permissions (IAM, Lambda, DynamoDB, Cognito, EventBridge, CloudWatch)
2. **AWS CLI** v2+
3. **SAM CLI** v1.80+
4. **PowerShell** (for deployment scripts)
5. **Stripe Account** with test/live keys
6. **AWS Region**: `eu-west-1` (default; modify parameter files to change)

## Directory Structure

```
infra/
├── sam/
│   ├── template.yaml                 # CloudFormation template (environment-agnostic)
│   ├── parameters/
│   │   ├── dev.json                  # Dev environment parameters
│   │   ├── test.json                 # Test environment parameters
│   │   └── prod.json                 # Prod environment parameters
│   └── build/                        # SAM build artifacts
├── scripts/
│   ├── deploy.ps1                    # Deployment script
│   └── teardown.ps1                  # Stack teardown script
└── iam/
	├── roles/
	│   └── lambda-execution-role.yaml # Lambda IAM role (included in main template)
	└── policies/
		└── lambda-invoke-policies.yaml # Lambda invoke policies (included in main template)
```

## Parameter Description

| Parameter | Dev | Test | Prod | Notes |
|-----------|-----|------|------|-------|
| **Env** | dev | test | prod | Environment name (affects resource naming, retention policies, billing) |
| **TableName** | zip02-tickets-dev | zip02-tickets-test | zip02-tickets | DynamoDB table name (must be unique per account/region) |
| **StripeWebhookSecretSsmPath** | /zip02/dev/stripe/webhook-secret | /zip02/test/stripe/webhook-secret | /zip02/prod/stripe/webhook-secret | SSM Parameter Store path for Stripe signing secret |
| **InternalSchedulerTokenSsmPath** | /zip02/dev/internal/scheduler-token | /zip02/test/internal/scheduler-token | /zip02/prod/internal/scheduler-token | Shared secret for EventBridge machine invocations |
| **CognitoRegion** | eu-west-1 | eu-west-1 | eu-west-1 | AWS region for Cognito (must match SAM deployment region) |
| **CognitoUserPoolId** | POPULATE_AFTER_STACK_DEPLOY | POPULATE_AFTER_STACK_DEPLOY | POPULATE_AFTER_STACK_DEPLOY | ⚠️ Set after first deployment; see "Post-Deployment Setup" |
| **CognitoClientId** | POPULATE_AFTER_STACK_DEPLOY | POPULATE_AFTER_STACK_DEPLOY | POPULATE_AFTER_STACK_DEPLOY | ⚠️ Set after first deployment; see "Post-Deployment Setup" |
| **CognitoOrganizerGroups** | organizer-admin,organizer-operator | organizer-admin,organizer-operator | organizer-admin,organizer-operator | Comma-separated list of Cognito groups required for organizer endpoints |
| **BillingMode** | PAY_PER_REQUEST | PAY_PER_REQUEST | PROVISIONED | DynamoDB billing mode (on-demand for dev/test; provisioned for prod cost predictability) |
| **LambdaMemoryMb** | 512 | 512 | 1024 | Lambda memory (higher for prod = faster execution, less time spent) |
| **LambdaTimeoutSeconds** | 29 | 29 | 29 | Lambda timeout (29s = under API Gateway 30s hard limit) |
| **LogRetentionDays** | 7 | 30 | 90 | CloudWatch Logs retention (short for dev cost savings; longer for prod audit trail) |

## Step 1: Create SSM Parameters

Before deploying, store Stripe keys and the scheduler token in AWS Systems Manager Parameter Store.

### 1.1 Get Stripe Webhook Signing Secret

From your Stripe Dashboard:
1. Go to **Developers** → **Webhooks**
2. Find your endpoint
3. Click **Reveal signing secret**
4. Copy the secret (format: `whsec_test_...` or `whsec_...`)

### 1.2 Create Internal Scheduler Token

Generate a random token (32+ bytes recommended):

```powershell
$token = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes((New-Guid).ToString() + (New-Guid).ToString()))
Write-Host $token
```

### 1.3 Store Secrets in SSM Parameter Store

**Dev environment:**

```bash
# Stripe webhook secret
aws ssm put-parameter \
  --name /zip02/dev/stripe/webhook-secret \
  --value "whsec_test_..." \
  --type SecureString \
  --overwrite

# Internal scheduler token
aws ssm put-parameter \
  --name /zip02/dev/internal/scheduler-token \
  --value "<YOUR_TOKEN_FROM_1.2>" \
  --type SecureString \
  --overwrite
```

**Test environment:**

```bash
aws ssm put-parameter \
  --name /zip02/test/stripe/webhook-secret \
  --value "whsec_test_..." \
  --type SecureString \
  --overwrite

aws ssm put-parameter \
  --name /zip02/test/internal/scheduler-token \
  --value "<YOUR_TOKEN>" \
  --type SecureString \
  --overwrite
```

**Prod environment:**

```bash
aws ssm put-parameter \
  --name /zip02/prod/stripe/webhook-secret \
  --value "whsec_live_..." \
  --type SecureString \
  --overwrite

aws ssm put-parameter \
  --name /zip02/prod/internal/scheduler-token \
  --value "<YOUR_TOKEN>" \
  --type SecureString \
  --overwrite
```

## Step 2: Deploy Stack (First Time - With Cognito Auto-Creation)

The SAM template automatically creates Cognito resources on first deployment. After deployment, you'll retrieve their IDs to configure the application.

```bash
# Dev
./infra/scripts/deploy.ps1 -Environment dev

# Test
./infra/scripts/deploy.ps1 -Environment test

# Prod (requires approval)
./infra/scripts/deploy.ps1 -Environment prod
```

The script will:
1. Build the .NET project
2. Validate the SAM template
3. Deploy CloudFormation stack
4. Output stack resources (Lambda ARN, API URL, DynamoDB table, **Cognito User Pool ID and App Client ID**)

## Step 3: Post-Deployment Setup (Critical!)

After the first deployment, retrieve Cognito IDs from CloudFormation outputs and update your parameter files.

### 3.1 Get Cognito IDs from CloudFormation Stack

```bash
# Dev environment
aws cloudformation describe-stacks \
  --stack-name zip02-dev \
  --query 'Stacks[0].Outputs[?OutputKey==`CognitoUserPoolId` || OutputKey==`CognitoAppClientId`]' \
  --output table

# Test environment
aws cloudformation describe-stacks \
  --stack-name zip02-test \
  --query 'Stacks[0].Outputs[?OutputKey==`CognitoUserPoolId` || OutputKey==`CognitoAppClientId`]' \
  --output table

# Prod environment
aws cloudformation describe-stacks \
  --stack-name zip02-prod \
  --query 'Stacks[0].Outputs[?OutputKey==`CognitoUserPoolId` || OutputKey==`CognitoAppClientId`]' \
  --output table
```

### 3.2 Update Parameter Files

Edit `infra/sam/parameters/{dev,test,prod}.json` and replace:
- `POPULATE_AFTER_STACK_DEPLOY` with the actual **CognitoUserPoolId**
- Same for **CognitoAppClientId**

Example for dev:

```diff
- { "ParameterKey": "CognitoUserPoolId",              "ParameterValue": "POPULATE_AFTER_STACK_DEPLOY" },
- { "ParameterKey": "CognitoClientId",                "ParameterValue": "POPULATE_AFTER_STACK_DEPLOY" },
+ { "ParameterKey": "CognitoUserPoolId",              "ParameterValue": "eu-west-1_abc12def34" },
+ { "ParameterKey": "CognitoClientId",                "ParameterValue": "5h1a2r3e4d5s6e7c8r9e0t" },
```

### 3.3 Deploy Again with Cognito Parameters

Now that Cognito IDs are populated, redeploy to enable JWT authentication:

```bash
# Dev (with Cognito params now active)
./infra/scripts/deploy.ps1 -Environment dev

# Test
./infra/scripts/deploy.ps1 -Environment test

# Prod
./infra/scripts/deploy.ps1 -Environment prod
```

**Why two deployments?**  
CloudFormation creates Cognito resources on first deploy. The template needs to know their IDs during subsequent deployments to configure the Lambda environment variables used for JWT validation in `Program.cs`.

## Step 4: Create Cognito Test Users

### 4.1 For Dev/Test (Optional)

You can skip Cognito users in dev/test and rely on the **scheduler token** for testing. The `OrganizerWrite` policy allows both:
- JWT tokens from organizer-admin/organizer-operator groups, OR
- Requests with the internal scheduler token header

### 4.2 For Prod

Create at least one production organizer user:

```bash
# Create user
aws cognito-idp admin-create-user \
  --user-pool-id eu-west-1_abc12def34 \
  --username organizer@example.com \
  --message-action SUPPRESS \
  --temporary-password "TempPass123!"

# Set permanent password
aws cognito-idp admin-set-user-password \
  --user-pool-id eu-west-1_abc12def34 \
  --username organizer@example.com \
  --password "SecurePass123!" \
  --permanent

# Add to organizer-admin group
aws cognito-idp admin-add-user-to-group \
  --user-pool-id eu-west-1_abc12def34 \
  --username organizer@example.com \
  --group-name organizer-admin
```

## Step 5: Test the Deployment

### 5.1 Get API Endpoint

```bash
aws cloudformation describe-stacks \
  --stack-name zip02-dev \
  --query 'Stacks[0].Outputs[?OutputKey==`ApiUrl`].OutputValue' \
  --output text
```

### 5.2 Test Without Auth (Should Work in Dev)

```bash
curl -X GET "https://<API_URL>/swagger/ui"
```

### 5.3 Test Scheduler Token (With Auth Required in Test/Prod)

```bash
# Get scheduler token from SSM
$token = $(aws ssm get-parameter --name /zip02/dev/internal/scheduler-token --with-decryption --query Parameter.Value --output text)

# Call reconciliation endpoint (protected by OrganizerWrite)
curl -X POST "https://<API_URL>/refunds/reconcile-ended-events" \
  -H "x-invocation-source: eventbridge-schedule" \
  -H "x-internal-auth: $token" \
  -H "Content-Type: application/json" \
  -d '{}'
```

## Troubleshooting

### Issue: CloudFormation Stack Creation Fails

**Cause**: Missing IAM permissions or invalid parameter values.

**Fix**:
1. Check IAM policy has `cloudformation:*`, `lambda:*`, `dynamodb:*`, `cognito-idp:*`
2. Verify AWS region matches parameter files (default: eu-west-1)
3. Check DynamoDB table name is not in use in another stack

```bash
# View stack creation events
aws cloudformation describe-stack-events --stack-name zip02-dev | jq '.StackEvents[] | select(.ResourceStatus=="CREATE_FAILED")'
```

### Issue: Lambda Cannot Write to DynamoDB

**Cause**: IAM role missing permissions.

**Fix**: Ensure `LambdaExecutionRole` in template.yaml includes DynamoDB policy. Check via:

```bash
aws iam get-role-policy --role-name zip02-dev-lambda-role --policy-name DynamoDBTableAccess
```

### Issue: JWT Validation Fails

**Cause**: `CognitoUserPoolId` or `CognitoClientId` not populated in parameter files.

**Fix**: Follow "Post-Deployment Setup" step 3 to update parameters and redeploy.

### Issue: EventBridge Reconciliation Not Triggering

**Cause**: Lambda permission for EventBridge not created, or scheduler token missing from SSM.

**Fix**:
1. Verify Lambda has `ReconciliationSchedulePermission` and `ReservationExpirySchedulePermission`
2. Confirm SSM parameters exist:

```bash
aws ssm get-parameter --name /zip02/dev/internal/scheduler-token --with-decryption
```

## Environment-Specific Notes

### Dev

- **Cost**: Minimal (on-demand DynamoDB, low memory Lambda, short log retention)
- **Cognito Auth**: Optional (falls back to pass-through if empty)
- **Use Case**: Local development, rapid iteration, short-lived resources

### Test

- **Cost**: Low-to-moderate (same as dev + longer log retention for audit)
- **Cognito Auth**: Recommended (validate JWT flow)
- **Use Case**: Integration testing, UAT, team testing before prod

### Prod

- **Cost**: Higher (provisioned DynamoDB, higher Lambda memory, 90-day logs, deletion protection)
- **Cognito Auth**: Required (enforced in OrganizerWrite policy)
- **Use Case**: Live environment, real transactions, strict SLAs

## Cleanup

To teardown a stack and release resources:

```bash
# Dev
./infra/scripts/teardown.ps1 -Environment dev

# Test
./infra/scripts/teardown.ps1 -Environment test

# Prod (requires confirmation)
./infra/scripts/teardown.ps1 -Environment prod
```

**Note**: Prod DynamoDB tables are retained on stack deletion (DeletionPolicy: Retain) for data safety.

## Next Steps

1. ✅ Deploy all three environments (dev, test, prod)
2. 📝 Create Cognito test users for prod organizers
3. 🔗 Integrate Stripe webhooks (point to API Gateway endpoint)
4. 🧪 Run end-to-end tests against dev
5. 📊 Set up CloudWatch alarms and dashboards
6. 📖 Create runbooks for operations (scaling, troubleshooting, incident response)

## References

- [AWS SAM Documentation](https://docs.aws.amazon.com/serverless-application-model/)
- [AWS Cognito User Pools](https://docs.aws.amazon.com/cognito/latest/developerguide/user-pools.html)
- [AWS Systems Manager Parameter Store](https://docs.aws.amazon.com/systems-manager/latest/userguide/systems-manager-parameter-store.html)
- [zip02 Architecture ADR-001 (DynamoDB Single-Table)](../decisions/adr-001-dynamodb-single-table.md)
