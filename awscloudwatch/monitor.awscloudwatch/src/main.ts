import { CloudWatchClient, ListMetricsCommand, PutMetricDataCommand } from "@aws-sdk/client-cloudwatch"
import { run, timed, Protocol, StepFunction } from '@metrist/protocol'

const proto = new Protocol()
const steps: Record<string, StepFunction> = {}
let configured = false;
const config = {
  namespace: 'NO-ENV',
  region: 'NO-REGION'
}

steps.SubmitEvent = async function() {
  configIfNeeded()
  const client = new CloudWatchClient({region: config.region})
  const command = new PutMetricDataCommand({
    Namespace: config.namespace,
    MetricData: [
      {
        MetricName: 'test',
        Unit: 'Kilobytes',
        Value: 42
      }
    ]
  })
  proto.sendTime(await timed(async () => {
    await client.send(command)
  }))
}

steps.GetEvent = async function() {
  configIfNeeded()
  const client = new CloudWatchClient({region: config.region})
  const command = new ListMetricsCommand({
    Namespace: config.namespace,
    MetricName: 'test'
  })
  // Not that we check for responsiveness of the API, not for the details of the answer. AWS will typically
  // have delays built in so if we want to do more in-depth testing we need to have things like "create a really
  // new metric and measure how long it takes to show up" which does not seem to represent a real-world use case.
  proto.sendTime(await timed(async () => {
    await client.send(command)
  }))
}

const configIfNeeded = function () {
  if (!configured) {
    process.env.AWS_ACCESS_KEY_ID = proto.getConfigValue('awscloudwatch', 'AWSAccessKeyID') ?? 'ACCESS-KEY-NOT-FOUND'
    process.env.AWS_SECRET_ACCESS_KEY = proto.getConfigValue('awscloudwatch', 'AWSSecretAccessKey') ?? 'SECRET-ACCESS-KEY-NOT-FOUND'

    config.region = proto.getConfigValue('awscloudwatch', 'AWSRegion') ?? process.env.ORCHESTRATOR_REGION ?? 'local-dev'

    const namespaceConfig = proto.getConfigValue('awscloudwatch', 'Namespace')
    if (namespaceConfig) {
      config.namespace = namespaceConfig
    } else {
      config.namespace = `monitors/awscloudwatch/${process.env.ENVIRONMENT_TAG ?? 'local'}/${config.region}`
    }

    configured = true
  }
}

async function main() {
  await run(proto, steps)
}

main()
