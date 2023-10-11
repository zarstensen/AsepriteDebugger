
local status, msg = xpcall(dofile, debug.traceback, app.params.test_script)

if not status then
    print(msg)
end
