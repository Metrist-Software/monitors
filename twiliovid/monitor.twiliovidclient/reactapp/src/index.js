import React from "react"
import ReactDOM from "react-dom"
import TwilioVideo from "./twiliovid"

const url = new URL(window.location)
const roomName = url.searchParams.get('roomName')
const token = url.searchParams.get('token')

ReactDOM.render(
  <TwilioVideo
    roomName={roomName}
    token={token}
  />,
  document.getElementById('root')
)
