import React from 'react';

import { ZoomMtg } from '@zoomus/websdk';

ZoomMtg.setZoomJSLib('https://source.zoom.us/2.8.0/lib', '/av');

ZoomMtg.preLoadWasm();
ZoomMtg.prepareWebSDK();
// loads language files, also passes any error messages to the ui
ZoomMtg.i18n.load('en-US');
ZoomMtg.i18n.reload('en-US');

function Zoom() {
  const url = new URL(window.location)
  const sdkKey = url.searchParams.get('sdkKey')
  const sdkSecret = url.searchParams.get('sdkSecret')
  const meetingNumber = url.searchParams.get('meetingNumber')
  const meetingPassword = url.searchParams.get('passWord')
  const userName = 'Canary'
  const leaveUrl =  '/index.html'
  const userEmail = 'zoom@canarymonitor.com'
  
  // 1 is host, 0 is participant. Allow us to send in an override if we want to do participant
  let role = 1 
  if (url.searchParams.get('role')) {
    role = parseInt(url.searchParams.get('role'))
  }

  ZoomMtg.generateSDKSignature({
    sdkKey: sdkKey,
    sdkSecret: sdkSecret,
    meetingNumber: meetingNumber,
    role: role,
    success: (signature) => {
      console.log(`Got signature`)
      console.log(signature)
      startMeeting(signature.result)
    },
    error: (error) => {
      console.log(error)
    }
  })

  function startMeeting(signature) {
    document.getElementById('zmmtg-root').style.display = 'block'

    ZoomMtg.init({
      leaveUrl: leaveUrl,
      disablePreview: true,
      disableJoinAudio: true,
      success: (success) => {
        console.log('INIT SUCCESS')
        ZoomMtg.join({
          meetingNumber: meetingNumber,
          userName: userName,
          signature: signature,
          sdkKey: sdkKey,
          userEmail: userEmail,
          passWord: meetingPassword,
          success: (success) => {
            console.log(success)
          },
          error: (error) => {
            console.log(error)
          }
        })

      },
      error: (error) => {
        console.log(error)
      }
    })
  }

  return (
    <div className="Zoom">
      <main>
        <h1>Zoom</h1>
      </main>
    </div>
  );
}

export default Zoom;
