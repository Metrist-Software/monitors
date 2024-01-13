import { randomUUID } from 'crypto'
import { Protocol, StepFunction, timed } from '@metrist/protocol'
import { axios } from '@metrist/axios_proxy'

const proto = new Protocol()
const steps: Record<string, StepFunction> = {}
const uuid: string = randomUUID()

const config: any = {
  clientId: null,
  clientSecret: null,
  domain: null,
  scope: null
}

const configCallback = async (_config: any) => {
  config.clientId = proto.getConfigValue('okta', 'ClientId')
  config.clientSecret = proto.getConfigValue('okta', 'ClientSecret')
  config.domain = proto.getConfigValue('okta', 'Domain')
  config.scope = proto.getConfigValue('okta', 'Scope')
}

const cleanupHandler = async () => {}

steps.GetToken = async () => {
  const url = `https://${config.domain}/oauth2/default/v1/token`
  const data = `grant_type=client_credentials&scope=${config.scope}`
  const auth = Buffer.from(`${config.clientId}:${config.clientSecret}`).toString('base64')
  proto.logInfo(`Obtaining machine token from ${url}`)
  const time = await timed(async () => {
    const response = await axios.post(
      url,
      data,
      {
        headers: {
          'authorization': `Basic ${auth}`,
          'accept': 'application/json',
          'cache-control': 'no-cache',
          'content-type': 'application/x-www-form-urlencoded',
        },
      }
    )
    if (response.status != 202 && response.status != 200) {
      proto.logError(`Unexpected response: ${JSON.stringify(response)}`)
      throw `Unexpected response ${response.status}`
    }
  })
  proto.logInfo(`GetToken completed in ${time}ms`)
  proto.sendTime(time)
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
