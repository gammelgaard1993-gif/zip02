# Post-Deployment Checklist

After running deploy.ps1, use this checklist to verify the stack is healthy and ready for testing.

## ✅ Deployment Success Verification

1. **Stack Creation/Update Completed**
   ```bash
   aws cloudformation describe-stacks --stack-name zip02-dev --query 'Stacks[0].StackStatus' --output text
   # Expected: CREATE_COMPLETE or UPDATE_COMPLETE
   ```

2. **Lambda Function Created**
   ```bash
   aws lambda get-function --function-name zip02-dev --query 'Configuration.FunctionArn' --output text
   ```

3. **DynamoDB Table Active**
   ```bash
   aws dynamodb describe-table --table-name zip02-tickets-dev --query 'Table.TableStatus' --output text
   # Expected: ACTIVE
   ```

4. **API Gateway Deployed**
   ```bash
   aws apigatewayv2 get-apis --query "Items[?Name=='zip02-dev'].ApiEndpoint" --output text
   ```

## 🔑 Retrieve Critical Outputs

After deployment, retrieve Cognito IDs and other stack outputs:

```bash
# Dev environment
aws cloudformation describe-stacks \
  --stack-name zip02-dev \
  --region eu-west-1 \
  --query 'Stacks[0].Outputs' \
  --output json
```

**Save these outputs** for the next step. Key values:
- `CognitoUserPoolId` → needed for application config
- `CognitoAppClientId` → needed for JWT validation
- `ApiUrl` → endpoint for testing

## ⚙️ Update Application Configuration

If this is your **first deployment**, the Cognito User Pool and App Client are now created. You must:

1. **Update Parameter Files** with actual Cognito IDs:
   ```bash
   # Get the IDs (from step above)
   # Edit infra/sam/parameters/dev.json and replace:
   # "CognitoUserPoolId": "POPULATE_AFTER_STACK_DEPLOY" 
   # with the actual ID from CloudFormation outputs

   # Example:
   # "CognitoUserPoolId": "eu-west-1_abc12def34"
   ```

2. **Redeploy with Cognito Parameters**:
   ```powershell
   .\infra\scripts\deploy.ps1 -Env dev
   ```

   This ensures the Lambda environment variables are configured with the Cognito IDs for JWT validation.

## 🧪 Test Basic Connectivity

### 1. Test API Health (No Auth Required in Dev)

```bash
API_URL=$(aws cloudformation describe-stacks --stack-name zip02-dev --query 'Stacks[0].Outputs[?OutputKey==`ApiUrl`].OutputValue' --output text)

# Test GET /swagger/ui (should return Swagger UI)
curl -s "$API_URL/swagger/ui" | head -20
```

### 2. Test EventBridge Scheduler Token (With Auth)

Get the scheduler token:
```bash
SCHEDULER_TOKEN=$(aws ssm get-parameter --name /zip02/dev/internal/scheduler-token --with-decryption --query Parameter.Value --output text)
echo "Token: $SCHEDULER_TOKEN"
```

Test the reconciliation endpoint (protected by OrganizerWrite):
```bash
curl -X POST "$API_URL/refunds/reconcile-ended-events" \
  -H "x-invocation-source: eventbridge-schedule" \
  -H "x-internal-auth: $SCHEDULER_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{}'
```

Expected response: 200 OK (or relevant error if no ended events exist)

### 3. Test DynamoDB Connectivity

Call a public endpoint (e.g., list events):
```bash
curl -X GET "$API_URL/events" \
  -H "Content-Type: application/json"
```

Expected response: 200 OK with empty array (first time) or list of events

## 🔍 Verify IAM Permissions

### Check Lambda Execution Role

```bash
ROLE_NAME=$(aws iam list-roles --query "Roles[?contains(RoleName, 'zip02-dev-lambda')].RoleName" --output text)
echo "Role: $ROLE_NAME"

# List attached policies
aws iam list-role-policies --role-name "$ROLE_NAME" --output table
```

**Expected policies**:
- DynamoDBTableAccess
- SsmStripeSecretRead
- CloudWatchLogsWrite
- XRayTracing

### Check Lambda Invoke Permissions

```bash
aws lambda get-policy --function-name zip02-dev --query 'Policy' | jq '.Statement[] | {Principal, Action}'
```

**Expected principals**:
- `apigateway.amazonaws.com` (API Gateway → Lambda)
- `events.amazonaws.com` (EventBridge → Lambda)

## 📊 Check CloudWatch Logs

### Tail Recent Logs

```bash
aws logs tail /aws/lambda/zip02-dev --follow
```

### Check for Errors

```bash
aws logs filter-log-events \
  --log-group-name /aws/lambda/zip02-dev \
  --filter-pattern "ERROR" \
  --query 'events[].message' \
  --output text
```

## 🔐 Cognito Setup (For Test/Prod)

### Create Test Organizer User

For **test/prod environments**, create test users:

```bash
POOL_ID=$(aws cloudformation describe-stacks \
  --stack-name zip02-test \
  --query 'Stacks[0].Outputs[?OutputKey==`CognitoUserPoolId`].OutputValue' \
  --output text)

# Create user
aws cognito-idp admin-create-user \
  --user-pool-id "$POOL_ID" \
  --username test-organizer@zip02.app \
  --message-action SUPPRESS \
  --temporary-password "TempPass123!"

# Set permanent password
aws cognito-idp admin-set-user-password \
  --user-pool-id "$POOL_ID" \
  --username test-organizer@zip02.app \
  --password "SecurePass123!" \
  --permanent

# Add to organizer-admin group
aws cognito-idp admin-add-user-to-group \
  --user-pool-id "$POOL_ID" \
  --username test-organizer@zip02.app \
  --group-name organizer-admin
```

### Get JWT Token

```bash
CLIENT_ID=$(aws cloudformation describe-stacks \
  --stack-name zip02-test \
  --query 'Stacks[0].Outputs[?OutputKey==`CognitoAppClientId`].OutputValue' \
  --output text)

aws cognito-idp admin-initiate-auth \
  --user-pool-id "$POOL_ID" \
  --client-id "$CLIENT_ID" \
  --auth-flow ADMIN_NO_SRP_AUTH \
  --auth-parameters USERNAME=test-organizer@zip02.app,PASSWORD=SecurePass123!
```

Use the `IdToken` from the response for Bearer token auth.

## 🚨 Troubleshooting

### Stack Creation Failed

Check CloudFormation events:
```bash
aws cloudformation describe-stack-events \
  --stack-name zip02-dev \
  --query 'StackEvents[?ResourceStatus==`CREATE_FAILED`]' \
  --output json
```

Common issues:
- Missing SSM parameters (Stripe webhook secret, scheduler token)
- Invalid IAM permissions
- Cognito domain name already taken (must be globally unique)

### Lambda Cold Start Warnings

Cold starts are expected. Monitor via X-Ray:
```bash
aws xray get-service-graph --start-time $(date -d '5 minutes ago' +%s) --end-time $(date +%s)
```

### EventBridge Not Triggering

Verify rule is enabled:
```bash
aws events describe-rule --name zip02-dev-reconciliation-schedule
# Check State: ENABLED
```

Verify Lambda has permission:
```bash
aws lambda get-policy --function-name zip02-dev | jq '.Statement[] | select(.Principal | contains("events"))'
```

### DynamoDB Throttling

If you see `ProvisionedThroughputExceededException`, check billing mode:
```bash
aws dynamodb describe-table --table-name zip02-tickets-dev --query 'Table.BillingModeSummary'
```

For prod, upgrade to PROVISIONED mode or increase on-demand limit.

## 📝 Next Steps

1. ✅ Verify all checks above pass
2. 📝 Document any custom configuration or secrets
3. 🧪 Run integration tests against the deployed stack
4. 📊 Set up CloudWatch alarms (Lambda errors, DynamoDB throttling, API latency)
5. 🔔 Enable SNS notifications for stack events
6. 📖 Create runbooks for common operations

## ⚡ Quick Command Reference

```bash
# Get stack status
aws cloudformation get-stack-status --stack-name zip02-dev

# View stack outputs
aws cloudformation describe-stacks --stack-name zip02-dev --query 'Stacks[0].Outputs' --output table

# View recent Lambda logs
aws logs tail /aws/lambda/zip02-dev --follow

# Invoke Lambda test
aws lambda invoke --function-name zip02-dev --payload '{}' /tmp/response.json

# Monitor DynamoDB
aws cloudwatch get-metric-statistics --namespace AWS/DynamoDB --metric-name ConsumedWriteCapacityUnits --dimensions Name=TableName,Value=zip02-tickets-dev --start-time 2024-01-01T00:00:00Z --end-time 2024-01-01T23:59:59Z --period 3600 --statistics Sum

# Check API Gateway throttling
aws apigatewayv2 get-apis --query "Items[?Name=='zip02-dev']" --output json
```

---

**For issues or questions, refer to:**
- [SETUP_GUIDE.md](./SETUP_GUIDE.md) — initial setup and parameter configuration
- [iam/README.md](./iam/README.md) — IAM role and policy documentation
- [../docs/getting-started/mvp-definition.md](../docs/getting-started/mvp-definition.md) — MVP scope and API endpoints
