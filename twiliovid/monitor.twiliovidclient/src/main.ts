import { AddressInfo } from 'net'
import { v4 as v4uuid } from 'uuid'
import { runInPuppeteer, startServer} from '@metrist/browser'
import { run, timed, Protocol, StepFunction } from '@metrist/protocol'
const twilio = require('twilio')

const proto = new Protocol()
const steps: Record<string, StepFunction> = {}
let configured = false
const config = {
  accountSid: '',
  authToken: '',
  apiKey: '',
  apiSecret: '',
  client: null as (typeof twilio | null)
}

const runPuppeteer = async (roomName: string, token: string, port: Number) => {
  return await runInPuppeteer(proto, async browser => {

    const page = await browser.newPage()
    page.setDefaultTimeout(0)
    proto.logDebug('New Page created')
    const url =
      `http://localhost:${port}` +
      `?roomName=${roomName}` +
      `&token=${token}`
    proto.logDebug("Joining with " + url)

    const timing = await timed(async () => {
      proto.logDebug('Navigating to page')
      await page.goto(url)
      proto.logDebug('Navigated to page')
      await page.waitForSelector('#status', {visible: true})
      proto.logDebug('Got selector #status')
    })
    proto.logDebug('Got timing')

    await browser.close()
    proto.logDebug('Browser closed')

    return timing
  })
}

steps.JoinRoom = async function() {
  configIfNeeded()
  proto.logInfo("Creating Twilio Video Room")
  const room = await config.client?.video.rooms.create({uniqueName: `Monitor2-${v4uuid()}`})
  if (!room) {
    throw "Could not create room"
  }

  proto.logDebug("Room: " + JSON.stringify(room))
  const server = await startServer(proto)
  const port = (server.address() as AddressInfo).port

  const AccessToken = require('twilio').jwt.AccessToken
  const VideoGrant = AccessToken.VideoGrant

  const token = new AccessToken(
    config.accountSid,
    config.apiKey,
    config.apiSecret,
    { identity: 'twiliovid_monitor', ttl: 14400 }
  )
  token.addGrant(new VideoGrant({
    room: room.uniqueName
  }))

  const tokenString = token.toJwt()
  proto.logDebug("JWT Token for joining: " + tokenString)

  proto.logInfo("Joining Twilio Video Room")
  try {
    const timeToJoin = await runPuppeteer(room.uniqueName, tokenString, port)
    proto.sendTime(timeToJoin)
  } catch (err: any) {
    proto.sendError('Caught error in puppeteer: ' + err.toString())
  }

  proto.logInfo("Completing Twilio Video Room")
  await room?.update({status: 'completed'})

  server.close()
}

const configIfNeeded = function() {
  if (!configured) {
    config.accountSid = proto.getConfigValue('twiliovid', 'AccountSid') ?? ''
    config.authToken = proto.getConfigValue('twiliovid', 'AuthToken') ?? ''
    config.apiKey = proto.getConfigValue('twiliovid', 'ApiKey') ?? ''
    config.apiSecret = proto.getConfigValue('twiliovid', 'ApiSecret') ?? ''
    config.client = twilio(config.accountSid, config.authToken);
    configured = true
  }
}

async function main() {
  await run(proto, steps)
}

main()
