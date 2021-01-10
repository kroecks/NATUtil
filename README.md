# NATUtil
A simple C# implementation of establishing a 2-way connection to verify two hosts can communicate with each other

To use, simply call NATConnection.EstablishConnection and provide the Public Ip, Local Ip, and Port of the destination host. Provide a callback lambda or function to determine what to do once you've established connection with the determined Ip.