/**
 * For Azure, we keep the secrets in AWS Secrets Manager in the default region for the environment
 * (prod: us-west-2, dev: us-east-1).
 *
 * This module helps in setting up credentials correctly from that secret.
 *
 * The code relies on `@aws-sdk/client-secrets-manager` and on `@azure/identity`
 */

import { SecretsManagerClient, GetSecretValueCommand } from '@aws-sdk/client-secrets-manager'
import { DefaultAzureCredential } from '@azure/identity'

interface Credentials {
  credential: DefaultAzureCredential,
  subscriptionId: string
}
export default async function setupCredentials(env: string, region: string) : Promise<Credentials> {
  if (region == 'local') {
    region = 'us-east-1'
  }
  const awsClient = new SecretsManagerClient({region: region })

  const secretPath = `/${env}/azure/api-token`
  const cmd = new GetSecretValueCommand({SecretId: secretPath})
  return await awsClient.send(cmd).then((response) => {
    const secret = JSON.parse(response.SecretString ?? '')

    // Simplest is to just go through the env.
    process.env.AZURE_TENANT_ID = secret["tenant-id"]
    process.env.AZURE_CLIENT_ID = secret["client-id"]
    process.env.AZURE_CLIENT_SECRET = secret["client-secret"]

    return {
      credential: new DefaultAzureCredential(),
      subscriptionId: secret["subscription-id"]
    }
  })
}
