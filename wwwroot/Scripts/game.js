const token = localStorage.getItem("token");
const userString = localStorage.getItem("user");
const user = JSON.parse(userString);
let gameState;
let yourDiskColor = "black";
let timer = null;
let timeLeft = 40; // Time per turn

// Chat
const messagesContainer = document.getElementById("messages");
const messageInput = document.getElementById("message");
const sendBtn = document.getElementById("send-btn");
const chatInputForm = document.getElementById("chat-input");

document.addEventListener("DOMContentLoaded", async () => {
  const board = document.getElementById("board");

  // Create 42 cells (7 columns x 6 rows)
  for (let i = 0; i < 42; i++) {
    const cell = document.createElement("div");
    cell.classList.add("cell");
    cell.id = i;
    if (i < 7) {
      cell.classList.add("cell-play");
    }
    board.appendChild(cell);
  }

  // Select all elements with class 'cell-play'
  const cells = document.querySelectorAll(".cell-play");

  // Add event listeners for mouseover and mouseout
  cells.forEach((cell) => {
    cell.addEventListener("mouseover", () => {
      if (!cell.querySelector(".circle")) {
        // Create disk and display it over cell
        const circle = document.createElement("div");
        circle.classList.add("circle");
        circle.style.backgroundColor = yourDiskColor;

        cell.appendChild(circle);
      }
    });

    cell.addEventListener("mouseout", () => {
      // Find circle inside cell and remove it
      const circle = cell.querySelector(".circle");
      if (circle) {
        circle.remove();
      }
    });

    cell.addEventListener("click", () => {
      // Send played move to server
      if (gameState && gameState.currentTurn !== user.Username) return;
      // Invalid move, whole column is filled
      if (cell.querySelector(".played-circle")) return;
      sendMove(cell.id);
    });
  });

  // Fetch player data
  const gameID = window.location.pathname.split("/").pop();

  // GAME SOCKET
  const socket = new WebSocket(`${API_URL}/ws/game/${gameID}?token=${token}`);

  socket.onopen = () => {
    console.log("WebSocket connection established");
  };

  const player1P = document.getElementById("player1");
  const player2P = document.getElementById("player2");

  socket.onmessage = (event) => {
    messageData = JSON.parse(event.data);
    console.log(messageData);

    if (messageData.currentTurn === user.Username) {
      console.log("YES");
      startTimer();
    } else {
      stopTimer(); // Stop timer if it's not your turn
    }

    if (messageData.board) {
      // Board related data
      updateGameState(messageData);
    } else if (messageData.player1 && messageData.player2) {
      // Initial game data
      initialGameData(messageData);
    } else {
      // End game data
      gameResultData(messageData);
    }
  };

  socket.onclose = () => {
    console.log("WebSocket connection closed");
    stopTimer();
  };

  socket.onerror = (error) => {
    console.error("WebSocket error:", error);
    stopTimer();
  };

  function sendMove(index) {
    socket.send(index);
    stopTimer();
  }

  function updateGameState(messageData) {
    messageData.board.forEach((cell, i) => {
      const cellDiv = document.getElementById(`${i}`);

      // Check if cellDiv is free before appending circle
      if (cellDiv && !cellDiv.querySelector(".played-circle")) {
        if (cell === 1 || cell === 2) {
          const circle = document.createElement("div");
          circle.classList.add("played-circle");
          circle.style.backgroundColor = cell === 1 ? "yellow" : "red"; // Set color based on the player

          const targetRow = Math.floor(i / 7); // Row number
          const targetTop = targetRow * 11.3 + 11.3; // Target top in vh (row height * row number) + offset to appear above cell

          // Set the custom animation
          const animationName = `moveDown-${i}`; // Unique animation name for each circle
          const styleSheet = document.styleSheets[0];
          styleSheet.insertRule(
            `
          @keyframes ${animationName} {
            0% {
              top: -${targetTop}vh; /* Start above the board */
            }
            100% {
              top: 0%; /* Target position is that cell */
            }
          }
        `,
            styleSheet.cssRules.length // insert at the bottom of the stylesheet
          );

          // Apply animation
          circle.style.animation = `${animationName} 1s ease forwards`;

          cellDiv.appendChild(circle);
        }
      }
    });

    if (messageData.winner != null) {
      ResultPopup(messageData.winner);
      stopTimer();
    }
  }

  function initialGameData(messageData) {
    player1P.textContent = user.Username;
    if (messageData.player1 === user.Username) {
      player2P.textContent = messageData.player2;
      yourDiskColor = "yellow";
    } else {
      player2P.textContent = messageData.player1;
      yourDiskColor = "red";
    }
    isInitialMessage = false;

    // Turn change UI
    TurnChange(messageData);
  }

  const popup = document.getElementById("gameResultPopup");
  const popupTitle = document.getElementById("popupTitle");
  const popupMessage = document.getElementById("popupMessage");
  const accountUpdates = document.getElementById("account_updates");
  function gameResultData(messageData) {
    // Show popup
    popup.classList.remove("hidden");

    const balanceP = document.createElement("p");
    balanceP.textContent = `New Balance: ${messageData.Balance}`;

    const skillScoreP = document.createElement("p");
    skillScoreP.textContent = `New skill score: ${messageData.SkillScore}`;

    accountUpdates.appendChild(balanceP);
    accountUpdates.appendChild(skillScoreP);

    // If user won on time it would be sent via user update data at WonOnTime
    if (messageData.WonOnTime) {
      ResultPopup(messageData.WonOnTime);
    }

    // Add event listener for close button, only after the popup is shown
    document
      .getElementById("closePopupButton")
      .addEventListener("click", () => {
        window.location.href = `/user/${user.Username}`;
      });
  }

  function ResultPopup(winner) {
    console.log(winner, "WINNER");
    if (winner === "None") {
      popupTitle.textContent = "It's a Draw!";
      popupMessage.textContent = "The game ended with no winner.";
    } else {
      popupTitle.textContent = `${winner} Wins! 🎉`;
      popupMessage.textContent = `${winner} has won the game! Congratulations!`;
    }
  }

  function TurnChange(messageData) {
    // Change current player turn color
    if (messageData.currentTurn === user.Username) {
      player1P.style.color = "#1bb933";
      player2P.style.color = "#fff";
    } else {
      player1P.style.color = "#fff";
      player2P.style.color = "#1bb933";
    }
  }

  // TIMER
  const timerValueElement = document.getElementById("timer-value");

  // Function to start or reset the timer
  function startTimer() {
    clearInterval(timer); // Clear any existing timer
    timeLeft = 40; // Reset time

    timer = setInterval(() => {
      timeLeft -= 1;
      timerValueElement.textContent = timeLeft;

      if (timeLeft <= 0) {
        clearInterval(timer);
      }
    }, 1000);
  }

  // Function to stop the timer
  function stopTimer() {
    clearInterval(timer);
  }

  // CHAT WEBSOCKET
  const chatSocket = new WebSocket(
    `${API_URL}/ws/chat/${gameID}?token=${token}`
  );

  chatSocket.onopen = () => {
    console.log("Connected to chat server.");
  };

  chatSocket.onmessage = (event) => {
    const message = event.data;
    displayMessage(message);
  };

  chatSocket.onclose = () => {
    console.log("Disconnected from the chat server.");
  };

  chatSocket.onerror = (error) => {
    console.error("Chat WebSocket error:", error);
  };

  // Enable send button only when there's a message
  messageInput.addEventListener("input", () => {
    sendBtn.disabled = !messageInput.value.trim();
  });

  // Send message
  chatInputForm.addEventListener("submit", (event) => {
    event.preventDefault();

    const message = messageInput.value.trim();
    if (message && chatSocket.readyState === WebSocket.OPEN) {
      chatSocket.send(message);
      messageInput.value = "";
      sendBtn.disabled = true;
    }
  });

  // Display message in chat
  function displayMessage(message) {
    // Split the received message into username and message content (message is received in format {username}: {message})
    const messageDiv = document.createElement("div");
    const usernameSpan = document.createElement("span");
    const messageSpan = document.createElement("span");
    const [username, ...messageTxt] = message.split(": ");

    // Username matches disk color
    if (username === user.Username && yourDiskColor === "red") {
      usernameSpan.style.color = "red";
    } else if (username === user.Username && yourDiskColor === "yellow") {
      usernameSpan.style.color = "yellow";
    } else if (username !== user.Username && yourDiskColor === "yellow") {
      usernameSpan.style.color = "red";
    } else {
      usernameSpan.style.color = "yellow";
    }

    usernameSpan.textContent = `${username}: `;
    messageSpan.textContent = messageTxt;
    messageDiv.appendChild(usernameSpan);
    messageDiv.appendChild(messageSpan);
    messagesContainer.appendChild(messageDiv);
  }
});
