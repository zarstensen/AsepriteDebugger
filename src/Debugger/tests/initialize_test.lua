require 'Debugger'

print("before init")

Debugger.connect(ASEDEB.config.endpoint)
Debugger.init()

print("after init")

ASEDEB.testAssert(debug.gethook() == Debugger._debugHook, "Debugger hook was not set!")
ASEDEB.testAssert(Debugger.handles[Debugger.initialize], "Initialize handle was not registered!")

ASEDEB.stopTest()
print("Finished tests")

ASEDEB.waitForServerStop()

print("server stopped")

Debugger.deinit()
