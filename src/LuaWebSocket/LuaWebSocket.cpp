#include "LUAWS.h"
#include "WebSocket.h"

#include <sstream>

extern "C"
{
	#include <lua.h>
	
	// entry point for when the shared library is required from a lua script.
	LUAWS_API int luaopen_LuaWebSocket(lua_State* L);
}

/// @brief Check the arguments passed to the current function have the same types as arg_types.
/// If not, or if the number of arguments does not match arg_types size, this function will raise a lua error.
void hasArgTypes(lua_State* L, std::vector<int> arg_types)
{
	int arg_count = lua_gettop(L);

	if (arg_count != arg_types.size())
	{
		std::stringstream err_msg;

		err_msg << "Invalid number of arguments passed to method.\nexpected '"
			<< arg_types.size()
			<< "' was '"
			<< arg_count
			<< "'";

		lua_pushstring(L, err_msg.str().c_str());
		lua_error(L);
	}

	for (int i = 0; i < arg_count; i++)
	{
		int arg_type = lua_type(L, i + 1);
		if (arg_type != arg_types[i])
		{
			std::stringstream err_msg;

			err_msg << "Invalid argument type for argument '"
				<< i + 1
				<< ".\nexpected '"
				<< lua_typename(L, arg_types[i])
				<< "' was '"
				<< lua_typename(L, arg_type)
				<< "'";

			lua_pushstring(L, err_msg.str().c_str());
			lua_error(L);
		}
	}
}

/// @brief Map field name to a lua_CFunction which wraps a WebSocket method.
std::unordered_map<std::string, lua_CFunction> method_map = {
	{"connect", [](lua_State* L) -> int {
		hasArgTypes(L, { LUA_TLIGHTUSERDATA, LUA_TSTRING });

		WebSocket* ws = static_cast<WebSocket*>(lua_touserdata(L, 1));
		std::string uri = lua_tostring(L, 2);

		try
		{
			ws->connect(uri);
		}
		catch(websocketpp::exception ex)
		{
			lua_pushstring(L, ex.what());
			lua_error(L);
		}

		return 0;
	}},

	{"close", [](lua_State* L) -> int {
		hasArgTypes(L, { LUA_TLIGHTUSERDATA });

		WebSocket* ws = static_cast<WebSocket*>(lua_touserdata(L, 1));

		try
		{
			ws->close();
		}
		catch(websocketpp::exception ex)
		{
			lua_pushstring(L, ex.what());
			lua_error(L);
		}

		return 0;
	}},

	{"isConnected", [](lua_State* L) -> int {
		hasArgTypes(L, { LUA_TLIGHTUSERDATA });

		WebSocket* ws = static_cast<WebSocket*>(lua_touserdata(L, 1));

		lua_pushboolean(L, ws->isConnected());

		return 1;
	}},

	{"send", [](lua_State* L) -> int {
		hasArgTypes(L, { LUA_TLIGHTUSERDATA, LUA_TSTRING });

		WebSocket* ws = static_cast<WebSocket*>(lua_touserdata(L, 1));
		std::string msg = lua_tostring(L, 2);

		try
		{
			ws->send(msg);
		}
		catch(websocketpp::exception ex)
		{
			lua_pushstring(L, ex.what());
			lua_error(L);
		}

		return 0;
	}},

	{"receive", [](lua_State* L) -> int {
		hasArgTypes(L, { LUA_TLIGHTUSERDATA });

		WebSocket* ws = static_cast<WebSocket*>(lua_touserdata(L, 1));

		std::optional<std::string> msg;

		try
		{
			msg = ws->receive();
		}
		catch(websocketpp::exception ex)
		{
			lua_pushstring(L, ex.what());
			lua_error(L);

			return 0;
		}

		if (msg)
			lua_pushstring(L, msg->c_str());
		else
			lua_pushnil(L);

		return 1;
	}},

	{"hasMessage", [](lua_State* L) -> int {
		hasArgTypes(L, { LUA_TLIGHTUSERDATA });

		WebSocket* ws = static_cast<WebSocket*>(lua_touserdata(L, 1));

		lua_pushboolean(L, ws->hasMessage());

		return 1;
	}},


};

/// @brief Use as LuaWebSocket in lua code.
/// Create an userdata value, pointing to a WebSocket.
/// The userdatas metatable contains a table for the __index field for various lua wrapper functions for WebSocket methods,
int createLuaWebSocket(lua_State* L)
{
	hasArgTypes(L, { });

	WebSocket* ws = new WebSocket();
	lua_pushlightuserdata(L, ws);
	
	// metatable for LuaWebSocket
	lua_newtable(L);

	// function table for metatable __index.
	lua_newtable(L);

	// convert method_map to a lua table
	for (const auto& [field, method] : method_map)
	{
		lua_pushcfunction(L, method);
		lua_setfield(L, 3, field.c_str());
	}

	// set metatable __index field to method_map lua table.
	lua_setfield(L, 2, "__index");

	// set metatable __name value
	std::string name = "LuaWebSocket";
	lua_pushstring(L, name.c_str());
	lua_setfield(L, 2, "__name");

	// make sure the WebSocket is deleted when lua object is garbage collected.
	lua_pushcfunction(L, [](lua_State* L) -> int {
		WebSocket* ws = static_cast<WebSocket*>(lua_touserdata(L, 1));
		delete ws;

		return 0;
	});
	
	lua_setfield(L, 2, "__gc");

	// assign metatable to WebSocket userdata.
	lua_setmetatable(L, 1);

	return 1;
}


int luaopen_LuaWebSocket(lua_State* L)
{
	lua_pushcfunction(L, createLuaWebSocket);
	lua_setglobal(L, "LuaWebSocket");
	
	return 0;
}