import showResponseMessage from "./resMessage.js";

const user = JSON.parse(localStorage.getItem("user"));
const leaderboardDiv = document.getElementById("leaderboard_container");
document.addEventListener("DOMContentLoaded", async () => {
  document.getElementById("menu").href = `/user/${user.Username}`;
  try {
    const response = await fetch(`${API_URL}/api/leaderboard`, {
      method: "GET",
    });

    if (response.ok) {
      const leaderboard = await response.json();
      // UI
      leaderboard.forEach((player, i) => {
        switch (i) {
          case 0:
            document.getElementById(
              "1st"
            ).textContent = `🥇 ${player.username} ${player.skillScore} pts`;
            break;
          case 1:
            document.getElementById(
              "2nd"
            ).textContent = `🥈 ${player.username} ${player.skillScore} pts`;
            break;
          case 2:
            document.getElementById(
              "3rd"
            ).textContent = `🥉 ${player.username} ${player.skillScore} pts`;
            break;
          default:
            const p = document.createElement("p");
            p.textContent = `${i + 1}th ${player.username} ${
              player.skillScore
            } pts`;
            leaderboardDiv.appendChild(p);
            break;
        }
      });
    }
  } catch (error) {
    showResponseMessage(`Error: ${error.message}`, "error");
  }
});
