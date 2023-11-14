#include "WebSocket.h"

WebSocket::WebSocket()
{
	m_client.init_asio();
	m_client.clear_access_channels(websocketpp::log::alevel::all);
}

void WebSocket::connect(const std::string& uri)
{
	std::error_code err;
	m_connection = m_client.get_connection(uri, err);

	if (err)
	{
		std::cout << err.message() << '\n';
	}

	m_connection->set_message_handler(std::bind(&WebSocket::onMessage, this, std::placeholders::_1, std::placeholders::_2));

	m_client.connect(m_connection);
	
	m_run_thread = std::thread(&Client::run, &m_client);

	while (m_connection->get_state() == websocketpp::session::state::connecting)
		std::this_thread::sleep_for(BLOCK_CHECK_INTERVAL);
}

void WebSocket::close()
{
	m_connection->close(websocketpp::close::status::normal, "");
	m_run_thread.join();

	// clear messages that has not yet been received.
	m_messages = {};
}

std::optional<std::string> WebSocket::receive()
{
	while (!hasMessage() && isConnected())
		std::this_thread::sleep_for(BLOCK_CHECK_INTERVAL);
	
	if (!isConnected())
	{
		return {};
	}

	std::string msg = m_messages.front();
	m_messages.pop();

	return msg;
}

