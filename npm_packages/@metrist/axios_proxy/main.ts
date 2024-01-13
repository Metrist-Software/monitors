import axios_ from 'axios'
import tunnel from 'tunnel'

const proxy = process.env.https_proxy || process.env.HTTPS_PROXY
const axiosWithProxy = () => {
  // There are two options: `host:port` or `<protocol>://host:port`. For
  // now, we only support the `host:port` one as that seems to be what gets
  // encountered in the wild for the vast majority of cases.
  const hostport = proxy!.split(':')
  const tunnelingAgent = tunnel.httpsOverHttp({
    proxy: {
      host: hostport[0],
      port: parseInt(hostport[1])
    }
  });
  return axios_.create({
    proxy: false,
    httpAgent: tunnelingAgent,
    httpsAgent: tunnelingAgent
  })
}

export const axios = proxy ? axiosWithProxy() : axios_.create()
