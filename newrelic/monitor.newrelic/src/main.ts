import { randomUUID } from 'crypto'
import { Protocol, StepFunction, timed, timedWithRetries } from '@metrist/protocol'
import { axios } from '@metrist/axios_proxy'

const proto = new Protocol()
const steps: Record<string, StepFunction> = {}
const uuid: string = randomUUID()
const syntheticMonitorPrefix = "metrist-mon"
const syntheticMonitorsApi = `https://synthetics.newrelic.com/synthetics/api/v3/monitors/`
let syntheticMonitorId: string;

const config: any = {
  accountNumber: null,
  insightApiKey: null,
  nerdgraphApiKey: null
}

const configCallback = async (_config: any) => {
  config.accountNumber = proto.getConfigValue('newrelic', 'NewRelicAccountNumber')
  config.insightApiKey = proto.getConfigValue('newrelic', 'NewRelicInsightAPIKey')
  config.nerdgraphApiKey = proto.getConfigValue('newrelic', 'NewRelicNerdGraphUSERKey')
}

const runQuery = async (query: string) => {
  const tryAgain = 'No results. Will try again.'
  proto.logInfo(`Will use query: ${query}`)
  const { totalTime } = await timedWithRetries(async () => {
    const response = await axios.post(
      'https://api.newrelic.com/graphql',
      {
        query: `{
          actor {
            nrql(query: "${query}", accounts: ${config.accountNumber}) {
            results
            }
          }
        }`
      },
      {
        headers: {
          'API-Key': config.nerdgraphApiKey,
          'Content-Type': 'application/json',
        }
      }
    )
    const resultSet = response.data.data.actor.nrql.results[0]
    if (typeof resultSet === 'undefined') {
      proto.logInfo(`No result yet, retrying.`)
      throw (tryAgain)
    }
    proto.logInfo('Match found, no more retries needed.')
  },
    (result): any => (result === tryAgain),
    0
  )

  proto.sendTime(totalTime)
}

const submit = async (url: string, data: any) => {
  proto.logInfo(`Sending to ${url}`)
  proto.logInfo(`    post data: ${JSON.stringify(data)}`)
  const time = await timed(async () => {
    const response = await axios.post(
      url,
      data,
      {
        headers: {
          'API-Key': config.insightApiKey,
          'Content-Type': 'application/json',
        },
      }
    )
    if (response.status != 202 && response.status != 200) {
      throw `Unexpected response ${response.status}`
    }
  })
  proto.sendTime(time)
}


steps.SubmitEvent = async () => {
  const event = {
    eventType: 'MetristMonitor',
    uuid: uuid
  }
  await submit(`https://insights-collector.newrelic.com/v1/accounts/${config.accountNumber}/events`, event)
}

steps.CheckEvent = async () => {
  const query = `SELECT timestamp FROM MetristMonitor WHERE uuid = '${uuid}'`
  await runQuery(query)
}

const timeStamp = Math.floor(Date.now() / 1000)
const instance = process.env.METRIST_INSTANCE_ID || 'fake-dev-instance'

steps.SubmitMetric = async () => {
  const metrics = [
    {
      metrics: [
        {
          name: "metrist.random",
          type: "gauge",
          value: Math.random(),
          timestamp: timeStamp,
          attributes: {
            instance: instance
          }
        }
      ]
    }
  ]
  await submit('https://metric-api.newrelic.com/metric/v1', metrics)
}

steps.CheckMetric = async () => {
  // Note: NR stores timestamps in millis, we have it in seconds, so that's why we slap three zeroes onto it.
  const query = `SELECT \`metrist.random\` FROM Metric SINCE 5 MINUTES AGO where instance = '${instance}' and timestamp = ${timeStamp}000`
  await runQuery(query)
}

steps.CreateSyntheticMonitor = async () => {
  const time = await timed(async () => {
    const response = await axios.post(syntheticMonitorsApi, JSON.stringify({
      "name": `${syntheticMonitorPrefix}-${uuid}`,
      "type": "SIMPLE",
      "frequency": 1,
      "uri": "https://newrelic.com",
      "locations": ["AWS_US_WEST_1"],
      "status": "ENABLED"
    }),
      {
        headers: {
          'API-Key': config.nerdgraphApiKey,
          'Content-Type': 'application/json',
        },
      }
    )

    syntheticMonitorId = response.headers.location.slice(syntheticMonitorsApi.length)
  })

  proto.sendTime(time)
}

steps.WaitForSyntheticMonitorResponse = async () => {
  const query = `SELECT responseCode FROM SyntheticRequest where monitorId='${syntheticMonitorId}' SINCE 30 MINUTES AGO`
  await runQuery(query)
}

steps.DeleteSyntheticMonitor = async () => {
  const time = await timed(async () => {
    await axios.delete(`${syntheticMonitorsApi}${syntheticMonitorId}`,
      {
        headers: {
          'API-Key': config.nerdgraphApiKey,
        },
      }
    )
  })

  proto.sendTime(time)
}

const cleanupHandler = async () => {
  type monitorListEntries = [{
    name: string
    createdAt: string,
    id: string
  }]

  const headers = {
    'API-Key': config.nerdgraphApiKey,
    'Content-Type': 'application/json',
  }

  const response = await axios.get<{ monitors: monitorListEntries }>(syntheticMonitorsApi, { headers })

  const deletePromises = response.data.monitors.filter((monitor) => {
    const createdAt = new Date(monitor.createdAt)
    const thirtyMinutesAgo = new Date(new Date().getTime() - 30 * 60 * 60)
    return monitor.name.startsWith(syntheticMonitorPrefix) && createdAt.getTime() < thirtyMinutesAgo.getTime()
  }).map(({ id }) => {
    return axios.delete(`${syntheticMonitorsApi}${id}`, { headers })
  })

  if (deletePromises.length == 0) {
    proto.logInfo(`Cleanup skipped. No stale monitors`)
    return
  }

  await Promise.all(deletePromises)
  proto.logInfo(`Cleanup done. Deleted ${deletePromises.length} synthetic monitors`)
}

async function main() {
  await proto.handshake(configCallback)
  let step: string | null = null
  while ((step = await proto.getStep(
    async () => { },
    cleanupHandler,
  )) != null) {
    proto.logDebug(`Starting step ${step}`)
    await steps[step]()
      .catch(err => proto.sendError(err))
  }
  proto.logDebug('Orchestrator asked me to exit, all done')
  process.exit(0)
}

main()
