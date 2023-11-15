
print("before init")

ASEDEB.Debugger.connect(ASEDEB.config.endpoint)
ASEDEB.Debugger.init()

print("after init")

ASEDEB.testAssert(debug.gethook() == ASEDEB.Debugger._debugHook, "Debugger hook was not set!")
ASEDEB.testAssert(ASEDEB.Debugger.handles[ASEDEB.Debugger.initialize], "Initialize handle was not registered!")

ASEDEB.stopTest()
print("Finished tests")

ASEDEB.waitForServerStop()

print("server stopped")

ASEDEB.Debugger.deinit()
