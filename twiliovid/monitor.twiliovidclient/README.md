# Twilio Video Monitor

Monitors the ability to join a Twilio call using a basic frontend provided by the
Twilio websdk and driven using headless Chrome with Puppeteer.

This monitor is broken into two parts:
  - The compiled React app with the Twilio video client
  - An Node backend using Express to serve the static React app and Puppeteer for automation

## Running locally

In order to run the monitor locally, you must first build the React app with

    make react

then you can run the monitor with Orchestrator.

See the [create-react-app readme](./reactapp/README.md) for its details

## Packaging

`make dist` handles the packaging of this monitor.
