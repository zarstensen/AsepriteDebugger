#pragma once

#include "LUAWS.h"

#include <string_view>
#include <queue>

#include <websocketpp/client.hpp>
#include <websocketpp/config/asio_no_tls_client.hpp>

/// @brief Class responsible for managing a single websocket client connection to a websocket server.
class WebSocket
{
public:
	using Client = websocketpp::client<websocketpp::config::asio_client>;
	
	/// @brief How long to wait, before checking if a function should stop blocking.
	static constexpr std::chrono::milliseconds BLOCK_CHECK_INTERVAL = std::chrono::milliseconds(50);
	
	inline WebSocket()
		{ m_client.init_asio(); }

	/// @brief Closes connection if currently connected.
	inline ~WebSocket()
		{ if (isConnected()) close(); }

	/// @brief Open a connection to the passed uri.
	LUAWS_API void connect(const std::string& uri);

	/// @brief closes the connection and waits until the client stops.
	LUAWS_API void close();

	inline bool isConnected()
		{ return m_connection->get_state() != websocketpp::session::state::closed; }
	
	/// @brief Send the passed string to the client, as a text message.
	/// undefined behaviour if isConnected() is false.
	inline void send(const std::string& msg)
		{ m_connection->send(msg); }

	/// @brief Returns the the earliest message received from the websocket server connection,
	/// which has not yet allready been received with this method.
	/// 
	/// If there exist no such message, this method blocks until a message is received, or the connection is closed.
	/// 
	/// If the connection is closed, this method will return all messages received up until connection closure,
	/// even after the connection has been closed. However, These messages are cleared when a new connection is opened.
	/// 
	/// @return does not have a value, if connection is closed and there exist no more messages to be handled.
	LUAWS_API std::optional<std::string> receive();

	/// @brief Return whether receive will block (false) or return instantly with a message (true).
	inline bool hasMessage()
		{ return m_messages.size() > 0; }

private:
	std::thread m_run_thread;
	Client m_client;
	Client::connection_ptr m_connection;

	std::queue<std::string> m_messages;
	
	inline void onMessage(websocketpp::connection_hdl hdl, Client::connection_type::message_type::ptr msg) 
		{ m_messages.push(msg->get_payload()); }
};