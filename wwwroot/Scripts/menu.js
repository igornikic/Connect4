import showResponseMessage from "./resMessage.js";

const token = localStorage.getItem("token");
const findMatch = document.getElementById("find_match");

document.addEventListener("DOMContentLoaded", async () => {
  // Ensure token exists
  if (!token) {
    window.location.href = "/";
    return;
  }

  // Extract username from URL
  const username = window.location.pathname.split("/").pop();

  try {
    const response = await fetch(`${API_URL}/api/user/${username}`, {
      method: "GET",
      headers: {
        Authorization: `Bearer ${token}`,
      },
    });

    if (response.ok) {
      const user = await response.json();
      // Save user to localStorage
      localStorage.setItem("user", JSON.stringify(user));

      // Update UI with retrieved user data
      const avatarImg = document.getElementById("avatar");
      avatarImg.src = `${API_URL}/api/user/${user.AvatarPath}`;
      avatarImg.alt = "Avatar";
      const usernameP = document.getElementById("username");
      usernameP.textContent = user.Username;
      const coinsP = document.getElementById("coins");
      coinsP.textContent = user.Balance;

      // Now display nav, which was hidden, to prevent layout shift
      document.getElementsByTagName("nav")[0].style.opacity = 1;
    } else if (response.status === 401) {
      showResponseMessage("Unauthorized. Redirecting to login...", "error");
      setTimeout(() => {
        window.location.href = "/";
      }, 2000);
    } else if (response.status === 404) {
      showResponseMessage("User not found.", "error");
      setTimeout(() => {
        window.location.href = "/";
      }, 2000);
    } else {
      const error = await response.text();
      showResponseMessage(`Error: ${error}`, "error");
      setTimeout(() => {
        window.location.href = "/";
      }, 2000);
    }
  } catch (error) {
    showResponseMessage(`Error: ${error.message}`, "error");
    setTimeout(() => {
      window.location.href = "/";
    }, 2000);
  }

  // Trigger matchmaking request
  findMatch.addEventListener("click", async () => {
    try {
      const user = JSON.parse(localStorage.getItem("user"));

      // Open WebSocket connection before matchmaking
      const socket = new WebSocket(`${API_URL}/ws/${username}`);

      socket.onopen = () => {
        console.log("Connected to WebSocket server.");

        // Matchmaking request
        fetch(`${API_URL}/api/game/queue/${user.Username}`, {
          method: "POST",
          headers: {
            Authorization: `Bearer ${token}`,
          },
        })
          .then(async (response) => {
            if (response.ok) {
              const queueMessage = await response.text();

              const queueP = document.getElementById("queue_message");
              queueP.textContent = queueMessage;
              findMatch.disabled = true;
            } else {
              showResponseMessage("Error finding match.", "error");
            }
          })
          .catch((error) => {
            showResponseMessage(`Error: ${error.message}`, "error");
          });
      };

      socket.onmessage = (event) => {
        const data = JSON.parse(event.data);
        let countdown = 3;
        showResponseMessage(`Opponent found, Match starting in 3s`, "success");
        // Create an interval to update countdown
        const interval = setInterval(() => {
          if (countdown > 0) {
            showResponseMessage(
              `Opponent found, Match starting in ${countdown}s`,
              "success"
            );
            countdown--;
          } else {
            clearInterval(interval);
            window.location.href = `/game/${data}`;
          }
        }, 1000);
      };

      socket.onclose = () => {
        showResponseMessage("Disconnected", "info");
        console.log("Disconnected");
        console.log("WebSocket connection closed.");
      };

      socket.onerror = (error) => {
        showResponseMessage(
          "an error occurred please try again later",
          "error"
        );
        console.log(`WebSocket error: ${error.message}`);
      };
    } catch (error) {
      showResponseMessage(`Error: ${error.message}`, "error");
    }
  });

  document.getElementById("logout-btn").addEventListener("click", (e) => {
    e.preventDefault();

    localStorage.clear();

    showResponseMessage("See you soon!", "success");
    setTimeout(() => {
      window.location.href = "/";
    }, 2000);
  });
});
