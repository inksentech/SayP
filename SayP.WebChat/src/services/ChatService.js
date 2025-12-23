import * as signalR from '@microsoft/signalr'

class ChatService {
  constructor() {
    this.connection = null
    this.onMessageReceived = null
    this.onTypingIndicator = null
    this.onConnectionChanged = null
  }

  async connect() {
    try {
      this.connection = new signalR.HubConnectionBuilder()
        .withUrl('http://localhost:5100/chatHub')
        .withAutomaticReconnect()
        .build()

      this.connection.on('ReceiveMessage', (message) => {
        if (this.onMessageReceived) {
          this.onMessageReceived(message)
        }
      })

      this.connection.on('TypingIndicator', (isTyping) => {
        if (this.onTypingIndicator) {
          this.onTypingIndicator(isTyping)
        }
      })

      this.connection.onreconnecting(() => {
        if (this.onConnectionChanged) {
          this.onConnectionChanged(false)
        }
      })

      this.connection.onreconnected(() => {
        if (this.onConnectionChanged) {
          this.onConnectionChanged(true)
        }
      })

      this.connection.onclose(() => {
        if (this.onConnectionChanged) {
          this.onConnectionChanged(false)
        }
      })

      await this.connection.start()
      
      if (this.onConnectionChanged) {
        this.onConnectionChanged(true)
      }

      console.log('✅ Connected to SayP Chat Hub')
    } catch (error) {
      console.error('❌ Connection failed:', error)
      if (this.onConnectionChanged) {
        this.onConnectionChanged(false)
      }
      
      // Retry connection after 5 seconds
      setTimeout(() => this.connect(), 5000)
    }
  }

  async sendMessage(data) {
    if (!this.connection || this.connection.state !== signalR.HubConnectionState.Connected) {
      throw new Error('Not connected to chat hub')
    }

    try {
      await this.connection.invoke('SendMessage', data)
    } catch (error) {
      console.error('Error sending message:', error)
      throw error
    }
  }

  async disconnect() {
    if (this.connection) {
      await this.connection.stop()
    }
  }
}

export default ChatService
