// Simple module to run some browser interaction through Puppeteer

import puppeteer, { Browser } from 'puppeteer-core'
import chromium from '@sparticuz/chromium'
import { Protocol } from '@metrist/protocol'

export async function runInPuppeteer<T>(proto: Protocol, browserFunction: (browser: Browser) => Promise<T>)  {
  proto.logInfo('Launching browser')

  let executablePath = await chromium.executablePath()
  let args = [...chromium.args, "--disable-notifications"]
  if (process.env.LOCAL_CHROMIUM) {
    executablePath = process.env.LOCAL_CHROMIUM
    args = (process.env.LOCAL_CHROMIUM_ARGS || "").split(/\s+/)
  }
  let headless = chromium.headless
  if (process.env.FORCE_HEADFUL) {
    headless = false
  }

  proto.logInfo(`browser executable path: ${executablePath}`)
  proto.logInfo(`browser args: ${args}`)
  proto.logInfo(`browser headless: ${headless}`)

  const browser = await puppeteer.launch({
     executablePath,
     args,
     headless,
     defaultViewport: {
       width: 800,
       height: 600,
     }
  })
  proto.logInfo('Browser launched')

  const result = await browserFunction(browser)

  await browser.close()
  proto.logInfo('Browser closed')

  return result
}

export { puppeteer }
export * as _puppeteer from 'puppeteer-core'
