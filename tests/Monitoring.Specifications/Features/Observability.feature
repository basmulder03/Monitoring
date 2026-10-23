Feature: The monitoring system shows its own state
  The system watches itself the way it watches applications, and can be scraped by others.

  Scenario: The system view shows the agent connected and nothing left unmonitored
    Given the environment is running
    And the dashboard shows the service "orders" in the scope "shop" as Healthy within 60 seconds
    Then the system view shows 1 of 1 agents connected within 30 seconds
    And the system view lists no pod without a monitoring sidecar within 30 seconds

  Scenario: The controller can be scraped for its own numbers
    Given the environment is running
    And the dashboard shows the service "orders" in the scope "shop" as Healthy within 60 seconds
    Then the metrics export of the controller says "monitoring_controller_agents_connected 1" within 30 seconds

  Scenario: The metrics of an instance are kept as history for the dashboard
    Given the environment is running
    And the dashboard shows the service "orders" in the scope "shop" as Healthy within 60 seconds
    Then the metric history of the instance "orders-1" in the scope "shop" has samples within 90 seconds
