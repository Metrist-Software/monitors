// Simple interface to run an Express server starting the
// monitor's bundled React app.
import express from 'express'
import { Server } from 'http'
import path from 'path'
import { Protocol } from '@metrist/protocol'
import exitHook from 'async-exit-hook';
import { AddressInfo } from 'net';

export function startServer(proto: Protocol) {
  return new Promise<Server>((resolve, _reject) => {
    proto.logDebug('starting server')
    const app = express()

    // Always use process.cwd() as we were doing for pkg'd apps __dirname will return 
    // paths like aws-serverless/shared/node_modules/@metrist/browser/reactapp/build/index.html
    let dirname = process.cwd()

    // __dirname or the pkg equivalent may include `build/jslib` so back that bit out again
    const pathname = dirname.endsWith('build/jslib')
      ? '../../reactapp/build'
      : 'reactapp/build'

    app.use(express.static(path.join(dirname, pathname)))

    app.use((_req, res, _next) => {
      res.sendFile(path.join(dirname, pathname, 'index.html'))
    })

    // Use 0 to listen on random available port to avoid conflicts between monitors
    const server = app.listen(0)
    server.on('listening', () => {
      const port = (server.address() as AddressInfo).port
      proto.logInfo(`server listening on port ${port}`)
      exitHook(() => {
        server.close()
      });
      resolve(server)
    })
  })
}
