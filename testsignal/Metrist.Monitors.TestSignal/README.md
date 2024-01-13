# TestSignal Monitor

This is a dummy monitor. It can be used to see whether our checks are
working, whether AWS is working, etcetera.

It contains three checks:

* The "Zero" check will return immediately and thus can act as a baseline
  value for seeing how much overhead there is on a check. Should be zero,
  or at least close to zero, hence the name.

* The "Normal" check will take 10 seconds plus or minus a normally distributed
  deviation.

* The "Poisson" check will take 10 seconds but according to a poisson distribution.

The latter two can be used to check statistical calculations, and in that sense
it provides our system a test signal.
