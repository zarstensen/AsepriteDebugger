print("HELLO HOW IS IT GOING :)")
print(debug.getinfo(1, "S").source)

require 'Debugger'

Debugger.init()

function wait(seconds)
	local start = os.time()
	repeat until os.time() > start + seconds
end

print("Debugger has initialized")
print(Debugger.ws)
