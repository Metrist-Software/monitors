import { Protocol, StepFunction, timeAndSend } from '@metrist/protocol'
import mysql = require('mysql2/promise')
const pg = require('pg');
import delay from "delay"


const config = {
  dbname: '',
  password: '',
  user: '',
  address: '',
  engine: '',
  port: '',
}


const proto = new Protocol()
const steps: Record<string, StepFunction> = {}

const teardownHandler = async function () {
  // DestroyInstance should do everything we could do, but
  // just in case that the step fails on a transient error,
  // we run it again


}

const cleanupHandler = async function () {

}


steps.PingInstance = async function () {
  proto.logDebug('Ping instance called!')

  await timeAndSend(proto, async () => {
    while (true) {
      if (config.engine != 'postgres') {
        try {
          const conn = await mysql.createConnection({
            host: config.address,
            user: config.user,
            password: config.password,
            database: config.dbname
          })
          await conn.execute('select 1 + 1 as sum')
          await conn.end()
          return
        }
        catch (e) {
          proto.logInfo(`Got exception while trying to connect to ${config.address}, retrying after sleep: ${e}`)
          await delay(1000)
        }

    }
    else {
        try {
          const client = new pg.Client({
            host: config.address,
            user: config.user,
            password: config.password,
            database: config.dbname,
            port: config.port
          })
          await client.connect()
          await client.query('SELECT 1+1 as sum', (err: any, res: any) => {
            client.end()
          })
          return
        }
        catch (e) {
          proto.logInfo(`Got exception while trying to connect to ${config.address}, retrying after sleep: ${e}`)
          await delay(1000)
        }

      }
    }
  })
}



const configCallback = async function (_config: any) {
  config.dbname = proto.getConfigValue('awsrds', 'dbname') ?? _config.dbname
  config.password = proto.getConfigValue('awsrds', 'password') ?? _config.password
  config.address = proto.getConfigValue('awsrds', 'address') ?? _config.address
  config.user = proto.getConfigValue('awsrds', 'user') ?? _config.user
  config.engine = proto.getConfigValue('awsrds', 'engine') ?? _config.engine
  config.port = proto.getConfigValue('awsrds', 'port') ?? _config.port


}

const main = async function () {
  await proto.handshake(configCallback)

  let step: string | null = null
  while ((step = await proto.getStep(cleanupHandler, teardownHandler)) != null) {
    proto.logDebug(`Starting step ${step}`)
    await steps[step]()
      .catch(e => proto.sendError(e))
  }
  proto.logInfo('Orchestrator asked me to exit, all done')
  process.exit(0)
}

main()
