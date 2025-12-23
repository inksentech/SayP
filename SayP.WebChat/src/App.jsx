import { useState, useEffect, useRef } from 'react'
import { Send, Phone, Settings, MessageCircle } from 'lucide-react'
import ChatService from './services/ChatService'
import './App.css'

function App() {
  const [messages, setMessages] = useState([])
  const [inputMessage, setInputMessage] = useState('')
  const [isConnected, setIsConnected] = useState(false)
  const [isTyping, setIsTyping] = useState(false)
  const [showSettings, setShowSettings] = useState(false)
  const [settings, setSettings] = useState({
    phoneNumber: '+905551234567',
    tenantId: '',
    companyId: '',
    backendUrl: 'http://localhost:5245'
  })
  const messagesEndRef = useRef(null)
  const chatService = useRef(null)

  useEffect(() => {
    // Initialize chat service
    chatService.current = new ChatService()
    
    // Setup event handlers
    chatService.current.onMessageReceived = (message) => {
      setMessages(prev => [...prev, {
        id: Date.now(),
        text: message.text,
        sender: 'bot',
        timestamp: new Date()
      }])
      setIsTyping(false)
    }

    chatService.current.onTypingIndicator = (isTyping) => {
      setIsTyping(isTyping)
    }

    chatService.current.onConnectionChanged = (connected) => {
      setIsConnected(connected)
    }

    // Connect
    chatService.current.connect()

    return () => {
      chatService.current?.disconnect()
    }
  }, [])

  useEffect(() => {
    scrollToBottom()
  }, [messages, isTyping])

  const scrollToBottom = () => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' })
  }

  const handleSendMessage = async (e) => {
    e.preventDefault()
    if (!inputMessage.trim()) return

    const userMessage = {
      id: Date.now(),
      text: inputMessage,
      sender: 'user',
      timestamp: new Date()
    }

    setMessages(prev => [...prev, userMessage])
    setInputMessage('')
    setIsTyping(true)

    try {
      await chatService.current.sendMessage({
        phoneNumber: settings.phoneNumber,
        message: inputMessage,
        tenantId: settings.tenantId || null,
        companyId: settings.companyId || null,
        backendUrl: settings.backendUrl
      })
    } catch (error) {
      console.error('Error sending message:', error)
      setIsTyping(false)
      setMessages(prev => [...prev, {
        id: Date.now(),
        text: 'Error: Could not send message. Please check your settings.',
        sender: 'system',
        timestamp: new Date()
      }])
    }
  }

  const formatTime = (date) => {
    return new Date(date).toLocaleTimeString('en-US', { 
      hour: '2-digit', 
      minute: '2-digit' 
    })
  }

  return (
    <div className="flex flex-col h-screen bg-whatsapp-gray">
      {/* Header */}
      <div className="bg-whatsapp-dark text-white px-4 py-3 flex items-center justify-between shadow-lg">
        <div className="flex items-center space-x-3">
          <div className="w-10 h-10 bg-whatsapp-green rounded-full flex items-center justify-center">
            <MessageCircle size={24} />
          </div>
          <div>
            <h1 className="font-semibold text-lg">SayP Chat Simulator</h1>
            <p className="text-xs text-gray-300">
              {isConnected ? (
                <span className="flex items-center">
                  <span className="w-2 h-2 bg-green-400 rounded-full mr-2"></span>
                  Connected
                </span>
              ) : (
                <span className="flex items-center">
                  <span className="w-2 h-2 bg-red-400 rounded-full mr-2"></span>
                  Disconnected
                </span>
              )}
            </p>
          </div>
        </div>
        <div className="flex items-center space-x-4">
          <Phone size={20} className="cursor-pointer hover:opacity-80" />
          <Settings 
            size={20} 
            className="cursor-pointer hover:opacity-80"
            onClick={() => setShowSettings(!showSettings)}
          />
        </div>
      </div>

      {/* Settings Panel */}
      {showSettings && (
        <div className="bg-white border-b border-gray-200 p-4 shadow-md">
          <h3 className="font-semibold mb-3 text-gray-800">Chat Settings</h3>
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">
                Phone Number
              </label>
              <input
                type="text"
                value={settings.phoneNumber}
                onChange={(e) => setSettings({...settings, phoneNumber: e.target.value})}
                className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-whatsapp-green focus:border-transparent"
                placeholder="+905551234567"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">
                Backend URL
              </label>
              <input
                type="text"
                value={settings.backendUrl}
                onChange={(e) => setSettings({...settings, backendUrl: e.target.value})}
                className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-whatsapp-green focus:border-transparent"
                placeholder="http://localhost:5245"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">
                Tenant ID (Optional)
              </label>
              <input
                type="text"
                value={settings.tenantId}
                onChange={(e) => setSettings({...settings, tenantId: e.target.value})}
                className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-whatsapp-green focus:border-transparent"
                placeholder="Leave empty for auto"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">
                Company ID (Optional)
              </label>
              <input
                type="text"
                value={settings.companyId}
                onChange={(e) => setSettings({...settings, companyId: e.target.value})}
                className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-whatsapp-green focus:border-transparent"
                placeholder="Leave empty for auto"
              />
            </div>
          </div>
        </div>
      )}

      {/* Messages Area */}
      <div className="flex-1 overflow-y-auto p-4 space-y-4">
        {messages.length === 0 && (
          <div className="text-center text-gray-500 mt-20">
            <MessageCircle size={64} className="mx-auto mb-4 opacity-50" />
            <p className="text-lg font-medium">Start a conversation</p>
            <p className="text-sm mt-2">Type a message to test the SayP AI</p>
          </div>
        )}

        {messages.map((message) => (
          <div
            key={message.id}
            className={`flex ${message.sender === 'user' ? 'justify-end' : 'justify-start'}`}
          >
            <div
              className={`max-w-xs lg:max-w-md px-4 py-2 rounded-lg shadow ${
                message.sender === 'user'
                  ? 'bg-whatsapp-light text-gray-800'
                  : message.sender === 'system'
                  ? 'bg-yellow-100 text-yellow-800'
                  : 'bg-white text-gray-800'
              }`}
            >
              <p className="text-sm whitespace-pre-wrap">{message.text}</p>
              <p className="text-xs text-gray-500 mt-1 text-right">
                {formatTime(message.timestamp)}
              </p>
            </div>
          </div>
        ))}

        {isTyping && (
          <div className="flex justify-start">
            <div className="bg-white px-4 py-2 rounded-lg shadow">
              <div className="flex space-x-2">
                <div className="w-2 h-2 bg-gray-400 rounded-full animate-bounce"></div>
                <div className="w-2 h-2 bg-gray-400 rounded-full animate-bounce" style={{animationDelay: '0.2s'}}></div>
                <div className="w-2 h-2 bg-gray-400 rounded-full animate-bounce" style={{animationDelay: '0.4s'}}></div>
              </div>
            </div>
          </div>
        )}

        <div ref={messagesEndRef} />
      </div>

      {/* Input Area */}
      <div className="bg-white border-t border-gray-200 px-4 py-3">
        <form onSubmit={handleSendMessage} className="flex items-center space-x-3">
          <input
            type="text"
            value={inputMessage}
            onChange={(e) => setInputMessage(e.target.value)}
            placeholder="Type a message..."
            className="flex-1 px-4 py-2 border border-gray-300 rounded-full focus:outline-none focus:ring-2 focus:ring-whatsapp-green focus:border-transparent"
            disabled={!isConnected}
          />
          <button
            type="submit"
            disabled={!isConnected || !inputMessage.trim()}
            className="bg-whatsapp-green text-white p-3 rounded-full hover:bg-opacity-90 disabled:bg-gray-300 disabled:cursor-not-allowed transition-colors"
          >
            <Send size={20} />
          </button>
        </form>
      </div>
    </div>
  )
}

export default App
