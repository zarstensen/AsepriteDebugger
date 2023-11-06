///
/// Tests for the WebSocket class, verifying it can actually send and receive messages.
///

#include <catch2/catch_test_macros.hpp>

#include <WebSocket.h>

#include <websocketpp/server.hpp>
#include <websocketpp/config/asio.hpp>

using Server = websocketpp::server<websocketpp::config::asio>;


TEST_CASE("LuaWebSocket.WebSocket.SendMessage")
{
	Server server;

	bool opened_connection = false;
	bool closed_connection = false;
	std::string received_msg;

	server.set_open_handler([&](websocketpp::connection_hdl hdl) {
		opened_connection = true;
		});

	server.set_close_handler([&](websocketpp::connection_hdl hdl) {
		closed_connection = true;
		});

	server.set_message_handler([&](websocketpp::connection_hdl hdl, Server::message_ptr msg) {
		received_msg = msg->get_payload();
		});

	server.init_asio();

	server.listen(8180);
	server.start_accept();

	std::thread server_thread = std::thread(&Server::run, &server);

	WebSocket socket;
	socket.connect("ws://localhost:8180");

	REQUIRE(opened_connection);

	socket.send("message");

	socket.close();

	REQUIRE(closed_connection);

	server.stop();

	server_thread.join();

	REQUIRE(received_msg == "message");
}

TEST_CASE("LuaWebSocket.WebSocket.ReceiveMessage")
{
	Server server;

	bool opened_connection = false;
	bool closed_connection = false;

	server.set_open_handler([&](websocketpp::connection_hdl hdl) {
		opened_connection = true;
		server.send(hdl, std::string("message"), websocketpp::frame::opcode::text);
		});

	server.set_close_handler([&](websocketpp::connection_hdl hdl) {
		closed_connection = true;
		});

	server.init_asio();

	server.listen(8181);
	server.start_accept();

	std::thread server_thread = std::thread(&Server::run, &server);

	WebSocket socket;
	socket.connect("ws://localhost:8181");

	REQUIRE(opened_connection);

	REQUIRE(socket.receive() == "message");

	socket.close();

	REQUIRE(closed_connection);

	server.stop();

	server_thread.join();
}


