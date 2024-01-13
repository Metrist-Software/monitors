# AWS Lambda Monitor

## Deployment

```sh
# Make sure the node modules are installed before running the 
# make apply
npm i --prefix testfunction

# Apply terraform changes
ENVIRONMENT_TAG=<env_tag> make init apply
```

