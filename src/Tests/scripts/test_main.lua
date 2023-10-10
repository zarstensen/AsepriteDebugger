io.output("/test_log.txt")

function print(...)

    local string_args = { }

    for _, arg in pairs({...}) do
        table.insert(string_args, tostring(arg))
    end

    io.write("ASE OUT: ", table.unpack(string_args), "\n")
    io.flush()
end

dofile(app.params.test_script)
