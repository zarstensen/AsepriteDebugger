
ASEDEB.Debugger.connect(ASEDEB.config.endpoint)

print("!<TEST LOG MESSAGE>!")

ASEDEB.stopTest()
ASEDEB.waitForServerStop()
ASEDEB.Debugger.deinit()
