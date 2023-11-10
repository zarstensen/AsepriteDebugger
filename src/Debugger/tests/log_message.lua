require 'Debugger'

Debugger.connect(ASEDEB.config.endpoint)

print("!<TEST LOG MESSAGE>!")

ASEDEB.stopTest()
