# Metrist Monitor Source Code

The code in this distribution enables Metrist customers to run their own active checks ("private monitoring").
This can be done for various reasons:

* To run checks that Metrist also runs but with customer-controlled API keys/configurations;
* To run checks that Metrist also runs but from customer premises to give a better view of networking impacts;
* To run checks that are fully specific to the customer, for example to run checks against vendors that Metrist
  does not support.

The code in this distribution enables all of these scenarios, and hopefully more.

## Rolling your own

Monitors can be written in any language that implements the monitoring protocol. At the time of this writing, we support two kinds
of monitors out of the box:

* .NET monitors, distributed as a DLL;
* NodeJS monitors, distributed as a `pkg` packaged executable (see for example the [Zoom client](monitor.zoomclient/) monitor).
* Terraform monitors, supported by NodeJS and also distributed as a `pkg` packaged executable (see for example the [AWS RDS](monitor.awsrds/) monitor).

The protocol implementation for any other language can be created in a matter of hours.

## Building, development

The code requires .NET Core 3.1, which you can download for free from https://dotnet.microsoft.com/download/dotnet-core/3.1
for Linux, macOS and Windows. We have organized the code as one "solution", so you can simply run `dotnet build` or
`dotnet test` to compile or test everything.

## Details on supported languages

### C# Monitors

Monitors written in C# are pretty straightforward DLL projects. A special [runner](Metrist.Runner) is
packaged with our agent and will take care of binding the DLL and executing the function within. Looking at a
[simple example](Metrist.Monitors.HubSpot) should give you a good idea of what is needed:

* Subclass `BaseMonitorConfig` for your configuration. Configuration will be passed from your monitor's configuration
  in our backend so you can add fields as you need;
* Subclass `BaseMonitor` with a constructor that accepts your configuration class. The constructor should store the
  configuration and can also do some setup that is shared between steps.
* Add a function per step. Step functions can be either synchronous or `async` and can either be void or return a float. The
  invocation code will look at the function signature when invoking the step function, and do the necessary `await` on async
  functions and will time void functions; float functions are expected to time themselves (this can be used if some expensive
  but irrelevant for timing setup needs to be done in the step; the step function can do the setup, and then call the actually
  to-be-timed action, preferably measuring time with one of the helper methods in the [base class](Metrist.Core/BaseMonitor.cs)).

### NodeJS Monitors

We prefer TypeScript, and a small library of helper functions are in [`jslib/`](jslib). Note due to how NodeJS works with modules,
imports, and packages, it is easiest to symlink this directory under your project's source directory. As with C#, a monitor is
pretty simple: we package it as an executable (using [`pkg`](https://www.npmjs.com/package/pkg)), the JS library has the protocol,
and all a `main.ts` or `index.ts` needs to do is start the protocol and then execute step functions. A helper function, called
`run`, will do most of the legwork making the main function basically:

``` typescript
import run from './jslib/common'
import Protocol from './jslib/protocol'

const proto = new Protocol()

const steps: Record<string, StepFunction> = {}
steps.MyStep = async function() {
...
}
const cleanupHandler = async function(_runCleanup: boolean) {
}
async function main() {
  await run(proto, steps, cleanupHandler)
}
main()
```

For now, all step functions are required to send timings through the protocol instance using `proto.sendTime(...)`. A simple
example can be found [here](monitor.zoomclient/src/index.ts).

There are two special helper modules in the JS library: a simple wrapper around an embedded HTTP server and a wrapper around
Puppeteer, NodeJS' browser control library. Using these two functions, it is very easy to build a browser-based monitor for
cases where a "pure API" does not exist; currently, we use this for the Twilio and Zoom video chat clients and the respective
monitor source code should make it clear how to proceed. Both contain a bundled React app for the embedded server (to serve the
page that the browser needs to open), but React is not mandatory here.

### Python monitors

Currently, there is [one monitor](monitor.snowflake) in Python and at this point in time, there is no library support. A new
Python monitor will require some refactoring to move common code into a library, and updating this document.

### Terraform monitors

Technically, these are hybrid NodeJS/Terraform monitors. These can be used for monitoring infrastructure that can be Terraformed
(which is currently pretty much anything). On top of the basic NodeJS monitor, these monitors have two Terraform "stacks":

* `infra`, which is a stack that is setup once and then can be left in place;
* `monitor`, which is the infrastructure that gets setup during a monitoring run (so that questions can be answered like: 'how
  long does it take for an ECS service to be available?')

There is a [small library](tflib/) with supporting terraform code which also has two parts:

* [`tflib/stacks`](tflib/stacks), which are Terraform stacks shared between multiple monitors;
* [`tflib/shared`](tflib/shared), which holds Terraform code that can be reused between monitors.

Currently, there is a single `infra` stack that sets up (at the time of writing) a single VPC to be shared by all monitors; in
the shared code, there is an `infra` _module_ that monitor code can import in order to get access to the shared outputs (again,
at this time, the VPC id).

A monitor usually has three regular step functions: the first to create the infrastructure; the second to check whether it is
alive (or wait for it); the third to destroy the infrastructure. Our current monitors also destroy the infrastructure on cleanup. Again,
perusing [an example](monitor.awsecs) will probably make things clearer.

### Directory structure

Directory structure in this repo for monitors is:

- <monitor_logical_name>
  - monitor-manifest.json (monitor manifests)
  - <package_folder1>
    - manifest.json (package manifest)
    - src files
  - <package_folder2>
    - manifest.json (package manifest)
    - src files

### Odds and ends


#### AWS permissions

AWS monitors run with AWS permissions from our agent; therefore, the agent must be started with all permissions required to execute
the Terraform code. Especially for the `infra` stack, this often results in a long list of permissions. Our solution is to only
give permissions for the `monitor` stack and run the `infra` stack manually, one-time. The monitor should run the infra stack
but have no deltas; if there are deltas and the monitor does not have permissions, it will error out and regular o11y channels
should alert someone to fix it.

#### Symlinking shared code

Code that needs to land in the zip file should be under the monitor; anything needed that is shared, like the Terraform shared code,
should therefore be symlinked into the monitor directory and used that way.
