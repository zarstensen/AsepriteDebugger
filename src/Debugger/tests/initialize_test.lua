require 'Debugger'

print("Performing test script")

Debugger.init()
Debugger.onConnect(function()
    testAssert(debug.gethook(), "Debugger hook was not set!")
    testAssert(Debugger.handles[Debugger.initialize], "Initialize handle was not registered!")
end)

