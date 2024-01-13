import sys
import time
import datetime
import pathlib
import protocol
import snowflake.connector

proto = protocol.Protocol()
now = int(time.time() * 1000)

conn = None

def setup():
  global conn

  conn = snowflake.connector.connect(
    user=proto.config['user'],
    password=proto.config['password'],
    account=proto.config['account']
  )

  conn.cursor().execute('CREATE WAREHOUSE IF NOT EXISTS canary_monitor_wh')
  conn.cursor().execute('ALTER WAREHOUSE canary_monitor_wh RESUME IF SUSPENDED')
  conn.cursor().execute('USE WAREHOUSE canary_monitor_wh')

def create_database():
  conn.cursor().execute(f'CREATE DATABASE monitor_{now}')
  conn.cursor().execute(f'USE DATABASE monitor_{now}')

def create_table():
  conn.cursor().execute('CREATE TABLE monitor_table(col1 integer, col2 string)')

def put_file():
  path = str(pathlib.Path.cwd().joinpath('sampledata.csv'))

  conn.cursor().execute(f'PUT file://{path} @%monitor_table')
  conn.cursor().execute('COPY INTO monitor_table')

def get_data():
  conn.cursor().execute('SELECT col1, col2 FROM monitor_table').fetchall()

def delete_data():
  conn.cursor().execute('DELETE FROM monitor_table WHERE col1 = 1')

def drop_table():
  conn.cursor().execute('DROP TABLE monitor_table')

def drop_database():
  conn.cursor().execute(f'DROP DATABASE monitor_{now}')


def cleanup(run_cleanup):
  proto.log_debug('In cleanup')

  if run_cleanup:
    proto.log_debug('Do cleanup')

    cutoff_time = datetime.datetime.now(tz=datetime.timezone.utc) - datetime.timedelta(minutes=30)

    for (created_at, name, _, _, _) in conn.cursor().execute("SHOW TERSE DATABASES STARTS WITH 'MONITOR_'"):
      if created_at < cutoff_time:
        proto.log_info(f'Cleanup dropping database {name}')
        conn.cursor().execute(f'DROP DATABASE {name}')

  conn.cursor().execute('ALTER WAREHOUSE canary_monitor_wh SUSPEND')

  conn.close()

steps = {
  'CreateDatabase': create_database,
  'CreateTable': create_table,
  'PutFile': put_file,
  'GetData': get_data,
  'DeleteData': delete_data,
  'DropTable': drop_table,
  'DropDatabase': drop_database
}

def main():
  proto.handshake()

  setup()

  step = None
  while (step := proto.get_step(cleanup)) is not None:
    proto.log_debug(f'Starting step {step}')
    time = protocol.timed(steps[step])
    proto.send_time(time)

  proto.log_info('Orchestrator asked me to exit, all done')
  sys.exit(0)

if __name__ == "__main__":
  main()
