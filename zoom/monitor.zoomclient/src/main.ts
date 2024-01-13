import {runInPuppeteer, startServer, _puppeteer} from '@metrist/browser'

import { Protocol, StepFunction, run, timed } from '@metrist/protocol'
import { AddressInfo } from 'net'

const proto = new Protocol()

const runPuppeteer = async (
  sdkKey: string,
  sdkSecret: string,
  meetingNumber: string,
  meetingPassword: string,
  port: Number
) => {
  return await runInPuppeteer(proto, async browser => {
    const page = await browser.newPage()

    page.setDefaultTimeout(0)
    proto.logInfo('New Page created')
    const url =
      `http://localhost:${port}` +
      `?sdkKey=${sdkKey}` +
      `&sdkSecret=${sdkSecret}` +
      `&meetingNumber=${meetingNumber}` +
      `&passWord=${meetingPassword}`

    let sdkHang = false
    const timing = await timed(async () => {
      let lastMsg = null;

      page.on('console', msg => {
        lastMsg = msg.text()
        proto.logInfo(`PAGE LOG: ${lastMsg}`)
      })

      await page.goto(url)
      proto.logInfo('Navigated to page')
      // div.video-avatar__avatar-name will show up blank until the name is populated and the joining is complete

      try {
        // We will wait for up to 100s for this xPath expression, after that we will check for a SDK hang or throw
        await page.waitForXPath("//div[@class='video-avatar__avatar-name' and contains(text(),'Canary')]", { timeout: 100 * 1000 })
        proto.logInfo('Successfully joined call')
      } catch (err) {
        sdkHang = confirmSDKHangOrThrow(err, lastMsg)
      }
    })

    // Only try to "leave" if we didn't get an SDK timeout
    if (!sdkHang) {
      proto.logInfo('Got timing')

      // Leave the meeting so there's no hanging sessions
      await page.waitForSelector('.footer__leave-btn')
      await page.click('.footer__leave-btn')
      const [button] = await page.$x(
        '//button[contains(.,\'Leave Meeting\') and contains(@class,\'zmu-btn\')]'
      )

      proto.logInfo('Clicking Button')
      await button.click()
    }

    proto.logInfo('Closing Browser')

    await browser.close()
    proto.logInfo('Browser closed')

    return {
        timing: timing,
        sdkHang: sdkHang
    }
  })
}

const steps: Record<string, StepFunction> = {}

steps.JoinCall = async function() {
  const sdkKey = proto.getConfigValue('zoom', 'SdkKey') ?? ''
  const sdkSecret = proto.getConfigValue('zoom', 'SdkSecret') ?? ''
  const meetingNumber = proto.getConfigValue('zoom', 'MeetingNumber') ?? ''
  const meetingPassword = proto.getConfigValue('zoom', 'MeetingPassword') ?? ''
  const server = await startServer(proto)

  const port = (server.address() as AddressInfo).port

  try {
    const {timing, sdkHang} = await runPuppeteer(
      sdkKey,
      sdkSecret,
      meetingNumber,
      meetingPassword,
      port
    )

    if (!sdkHang) {
      proto.sendTime(timing)
    } else {
      proto.logInfo("SDK hang detected, not submitting telemetry. Exiting")
      process.exit(0)
    }

  } catch (err: any) {
    proto.sendError(err.toString())
  }

  server.close()
  return
}

async function main() {
  await run(proto, steps)
}

function confirmSDKHangOrThrow(err : any, lastMsg : string | null) : boolean {
  if (err instanceof _puppeteer.TimeoutError) {
    // Every time we see the SDK hang, it hangs on one of these as the last PAGE LOG message. Any other timeout should be thrown as an actual monitor error
    if (lastMsg == "pre load wasm success: https://source.zoom.us/2.8.0/lib/av/1501_video.decode.wasm" || lastMsg == "JSHandle@error") {
      proto.logInfo(`SDKHang timeout error. Last msg was ${lastMsg}.`)
      return true
    } else {
      proto.logInfo(`puppeteer timeout detected that doesn't match the typical hang output. lastMsg was ${lastMsg}.`)
      // Timeout error by default shows details of the xpath expression. Throw more generic error.
      throw new Error("Timeout while trying to join call.")
    }
  }

  // Any other error, we rethrow
  throw err
}

main()
