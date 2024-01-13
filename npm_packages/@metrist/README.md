NPM Packages
------------
Contains all the packages that is published to NPM

## Contributing

To speed up developing locally, you can make changes to a package and link it to the monitor. For example:

1. If you made change to `protocol` package, `cd protocol`
2. Run `npm run build` to compile
3. Run `npm link`

> npm link in a package folder with no arguments will create a symlink in the global folder
> {prefix}/lib/node_modules/<package> that links to the package where the npm link command was executed.
> It will also link any bins in the package to {prefix}/bin/{name}. Note that npm link uses the global prefix

4. cd to the monitor's directory and run `npm link @metrist/protocol`. This will use linked `@metrist/protocol` from your local
5. Once you are done testing you can run the following to unlink and install the package from NPM registry

        npm unlink @metrist/protocol
        npm install


## Publishing

1. Increase the package version in `package.json` for the package
2. Run `npm login` and provide your npm credentials
3. To publish a package you can run `npm publish` from its directory. For example

        cd protocol
        npm publish

## Adding packages

Just add them and follow the steps above, but to remember to run `npm publish --access public` the first time to mark the package as public.
