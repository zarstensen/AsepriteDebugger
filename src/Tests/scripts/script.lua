require 'Debugger'

Debugger.init()

function wait(seconds)
	local start = os.time()
	repeat until os.time() > start + seconds
end

print("Debugger has initialized")
print(Debugger.ws)
