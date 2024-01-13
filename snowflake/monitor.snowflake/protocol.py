import sys
import re
import json
import os
from timeit import default_timer as timer
from typing import Callable

MAJOR = 1
MINOR = 1

def write(msg:str):
  print(f'{utf8len(msg):05} {msg}', file = sys.stdout, flush=True, end='')

def utf8len(s):
    return len(s.encode('utf-8'))

def __read__(nbytes: int):
  v = None
  while (v is None):
    if (sys.stdin.buffer.readable is False):
      raise Exception('stdin not readable, giving up')
    v = sys.stdin.buffer.read(nbytes)
  return v.decode('utf-8')

def read():
  length = int(__read__(6))
  msg = __read__(length)
  return msg

def expect(pattern):
  msg = read()
  result = re.search(pattern, msg)

  if result is None:
    raise Exception(f'Unexpected message; wanted={pattern}, received={msg}')

  return result

def snake_and_upcase(name: str):
  return re.sub(r'[A-Z]', '_\1', name).upper()

def timed(fnc: Callable[[], any]):
  start = timer()
  fnc()
  end = timer()
  return (end - start) * 1000

class Protocol:
  def handshake(self):
    write(f'Started {MAJOR}.{MINOR}')
    groups = expect(r'Version ([0-9]+)\.([0-9]+)')

    self.major = int(groups[1])
    self.minor = int(groups[2])
    self.assertCompatibility()

    write('Ready')

    groups = expect(r'Config (.*)')
    self.config = json.loads(groups[1])

    write('Configured')

  def get_config_value(self, monitor_logical_name, property_name):
    ret_val = None

    self.log_debug(f'Configure {monitor_logical_name}.{property_name}')

    if property_name in self.config:
      self.log_debug('- set value from runner config')
      ret_val = self.config.get(property_name)

    env_name = f'CANARY{snake_and_upcase(monitor_logical_name)}{snake_and_upcase(property_name)}'
    env_val = os.getenv(env_name)
    if env_val is not None:
      self.log_debug(f'- set/override value from env var {env_name}')
      ret_val = env_val

    return ret_val

  def get_step(self, cleanup_handler: Callable[[bool], any]):
    msg = read()
    if (msg == 'Exit 0'):
      cleanup_handler(False)
      return None

    if (msg == 'Exit 1'):
      cleanup_handler(True)
      return None

    if (msg.startswith('Run Step')):
      parts = msg.split(' ')
      return parts[2]

    self.log_error(f'Unexpected message "{msg}", assuming we have to exit')
    return None

  def assertCompatibility(self):
    is_compatible = self.major == MAJOR and self.minor >= MINOR
    if not is_compatible:
      raise Exception(f'Protocol incompatible. We support {MAJOR}.{MINOR}, orchestration supports {self.major}.{self.minor}.')

  def send_time(self, time):
    write(f'Step Time {time}')

  def send_ok(self):
    write('Step OK')

  def send_error(self, error):
    write(f'Step Error {error}')

  def log_debug(self, msg):
    write(f'Log Debug {msg}')

  def log_info(self, msg):
    write(f'Log Info {msg}')

  def log_warning(self, msg):
    write(f'Log Warning {msg}')

  def log_error(self, msg):
    write(f'Log Error {msg}')
