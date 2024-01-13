import React from 'react'

export default class TwilioVideo extends React.Component {
  constructor(props) {
    super(props)
    this.state = {connected: false}
  }
  async componentDidMount() {
    console.log('Properties: ' + JSON.stringify(this.props))
    var Video = require('twilio-video')
    const { roomName, token } = this.props
    const self = this
    Video.connect(token, {
      name: roomName,
      video: false,
      audio: false
    }).then(function(room) {
      self.setState({connected: true})
      room.once('disconnected', function() {
        console.log('Disconnected! This is not expected.')
        this.setState({connected: false})
      })
    }).catch(error => {
      console.log('Could not connect to the Room:', error.message)
      throw error
    })
  }

  render() {
    const visibility = this.state.connected ? 'visible' : 'hidden'
    return (
      <div>Twilio Video <span id="status" style={{visibility: visibility}}>Connected</span></div>
    )
  }
}
