# You may encounter a problem with openssl configuration on Ubuntu 22.

If `make test_run` on your computer results in an error like the following:

```
library: 'DSO support routines',
function: 'dlfcn_load',
reason: 'could not load the shared library',
code: 'ERR_OSSL_DSO_COULD_NOT_LOAD_THE_SHARED_LIBRARY'
```

The workaround provided by Zoho, [discussed here](https://askubuntu.com/questions/1409458/openssl-config-cuases-error-in-node-js-crypto-how-should-the-config-be-updated), worked for me.

**Note**: That workaround might solve the problem during `test_run` but may cause `make dist` to fail. I had to undo the above workaround for `make dist` to succeed.
