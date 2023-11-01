local PipeWebSocket = ASEDEB.PipeWebSocket

local pipe_ws = PipeWebSocket.new(ASEDEB.config.pipe_ws_path)
pipe_ws:connect("127.0.0.1", ASEDEB.config.endpoint)

local msg = pipe_ws:receiveJson()

ASEDEB.testAssert(
    msg.type == "test_message",
    "Received invalid message."
)
