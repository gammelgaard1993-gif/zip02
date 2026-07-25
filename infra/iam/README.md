# IAM Setup for zip02

## Overview

This directory contains IAM role and policy definitions for the zip02 ticketing system. All policies follow the **principle of least privilege**: each role grants only the minimum permissions needed for its specific function.

## Directory Structure

```
infra/iam/
├── roles/
│   └── lambda-execution-role.yaml        # Lambda execution role with all service permissions
├── policies/
│   └── lambda-invoke-policies.yaml       # Resource-based policies (API Gateway, EventBridge → Lambda)
└── README.md                             # This file
```

## Roles

### Lambda Execution Role (`lambda-execution-role.yaml`)

**Used By**: Lambda function hosting the zip02 API

**Permissions**:

| Service | Actions | Scope | Condition |
|---------|---------|-------|-----------|
| CloudWatch Logs | CreateLogGroup, CreateLogStream, PutLogEvents | Log group: `/aws/lambda/zip02*` | — |
| DynamoDB | GetItem, PutItem, UpdateItem, Query, Scan | Table & GSI ARNs only | Region match |
| DynamoDB Streams | GetRecords, GetShardIterator, DescribeStream, ListStreams | Stream ARN only | Region match |
| EventBridge | PutEvents | Event bus ARN | DetailType = "Scheduled Event" |
| SNS | Publish | Topics: `zip02-*` | — |
| SES | SendEmail, SendRawEmail | Any identity | From: noreply@, support@ |
| Secrets Manager | GetSecretValue | Secrets: `zip02/*` | VersionStage = AWSCURRENT |
| X-Ray | PutTraceSegments, PutTelemetryRecords | All | — |
| CloudWatch Metrics | PutMetricData | All | Namespace = zip02 |

**Why These Permissions?**

- **CloudWatch Logs**: Application logging and error tracking
- **DynamoDB**: MVP data storage (events, tickets, payments, notifications)
- **EventBridge**: Post-event scheduled reconciliation triggers
- **SNS**: Future event notifications (decoupling)
- **SES**: Transactional email (QR delivery, refund notifications)
- **Secrets Manager**: Secure configuration (Cognito IDs, Stripe webhook secret, scheduler token)
- **X-Ray**: Optional distributed tracing for performance debugging
- **CloudWatch Metrics**: Custom business metrics (ticket transitions, refund decisions, geofence rejections)

### Resource-Based Policies (`lambda-invoke-policies.yaml`)

**Attached To**: Lambda function

**Grants**:

1. **API Gateway → Lambda**: Allows HTTP API to invoke function for all incoming requests
2. **EventBridge → Lambda**: Allows scheduled reconciliation rule to invoke function on a schedule

## Integration with SAM Template

The main SAM template (`infra/sam/template.yaml`) should import these role definitions:

```yaml
Resources:
  # Import Lambda execution role
  LambdaExecutionRole:
	Type: AWS::IAM::Role
	Properties:
	  # ... (defined in lambda-execution-role.yaml)

  ApiFunction:
	Type: AWS::Serverless::Function
	Properties:
	  Role: !GetAtt LambdaExecutionRole.Arn  # Reference the role
	  # ... other properties
```

## Parameter-Driven IAM

For multi-environment support (dev/test/prod), the role names and resource ARNs are parameterized:

```yaml
Parameters:
  Environment:
	Type: String
	Default: dev
	AllowedValues: [dev, test, prod]
	Description: Deployment environment

Resources:
  LambdaExecutionRole:
	Properties:
	  RoleName: !Sub 'zip02-lambda-execution-role-${Environment}'
	  Tags:
		- Key: Environment
		  Value: !Ref Environment
```

## Deployment

Roles are deployed as part of the main SAM stack:

```bash
# Dev environment
sam deploy --parameter-overrides Environment=dev

# Test environment
sam deploy --parameter-overrides Environment=test

# Production environment
sam deploy --parameter-overrides Environment=prod
```

## Security Best Practices

### What This Role Does NOT Grant

- ❌ S3 access (no file storage in MVP)
- ❌ RDS access (DynamoDB only)
- ❌ Cognito administration (JWT validation only)
- ❌ Cross-account access (no AssumeRole)
- ❌ IAM modification (no iam:* actions)
- ❌ Lambda layer updates (only invocation)

### Audit & Monitoring

All IAM actions can be audited via CloudTrail:

```bash
# View recent IAM role assumptions
aws cloudtrail lookup-events \
  --lookup-attributes AttributeKey=ResourceName,AttributeValue=zip02-lambda-execution-role
```

### Credential Rotation

Secrets Manager handles credential rotation automatically:

```bash
# View stored secrets
aws secretsmanager list-secrets --filters Key=name,Values=zip02/

# Rotate a secret manually (if needed)
aws secretsmanager rotate-secret --secret-id zip02/stripe-webhook-secret
```

## Adding New Permissions

If a new feature requires additional permissions:

1. ✏️ Add the new statement to `lambda-execution-role.yaml`
2. 🧪 Test in dev environment with limited scope
3. 📊 Monitor CloudWatch Logs for `AccessDenied` errors
4. 📄 Update this README with the new permission and justification
5. ✅ Commit and review before deploying to test/prod

**Example: Adding S3 Access (Hypothetical)**

```yaml
S3ReadPolicy:
  Type: AWS::IAM::Policy
  Properties:
	PolicyName: zip02-s3-read-policy
	PolicyDocument:
	  Version: '2012-10-17'
	  Statement:
		- Sid: ReadRefundImageProofs
		  Effect: Allow
		  Action:
			- s3:GetObject
		  Resource: 
			- !Sub 'arn:aws:s3:::zip02-${Environment}-uploads/refunds/*'
	Roles:
	  - !Ref LambdaExecutionRole
```

## Testing IAM Policies

Dry-run API calls to validate permissions before deploying:

```bash
# Dry-run a DynamoDB PutItem to verify role has permission
aws dynamodb put-item \
  --table-name api-resources \
  --item '{"PK":{"S":"TEST"},"SK":{"S":"TEST"}}' \
  --dry-run
```

## References

- [AWS IAM Best Practices](https://docs.aws.amazon.com/IAM/latest/UserGuide/best-practices.html)
- [Principle of Least Privilege](https://docs.aws.amazon.com/IAM/latest/UserGuide/best-practices.html#grant-least-privilege)
- [SAM Permission Boundaries](https://docs.aws.amazon.com/serverless-application-model/latest/developerguide/serverless-policy-templates.html)
- [EventBridge Service Principals](https://docs.aws.amazon.com/eventbridge/latest/userguide/eb-service-principals.html)
